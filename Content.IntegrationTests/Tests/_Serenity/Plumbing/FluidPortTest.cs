using System.Linq;
using Content.Server._Serenity.Plumbing;
using Content.Server._Starlight.Plumbing.Components;
using Content.Shared._Serenity.Plumbing;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Serenity.Plumbing;

[TestFixture]
public sealed class FluidPortTest
{
    private const string Port = "PlumbingFluidPort";
    private const string Duct = "PlumbingDuctStraight";
    private const string FullTank = "WaterTankFull";
    private const string EmptyTank = "WaterTank";

    private static FixedPoint2 Volume(IEntityManager entMan, EntityUid tank)
    {
        Assert.That(entMan.System<SharedSolutionContainerSystem>().TryGetSolution(tank, "tank", out _, out var solution));
        return solution!.Volume;
    }

    private static Entity<PlumbingPortableComponent> Tank(IEntityManager entMan, EntityUid uid)
    {
        return (uid, entMan.GetComponent<PlumbingPortableComponent>(uid));
    }

    /// <summary>
    ///     Two ports joined by a duct. A tank set to supply on one port feeds a tank set to fill on the other;
    ///     a tank anchored on bare floor stays anchored but never connects. The valve can be set from the port.
    /// </summary>
    [Test]
    public async Task DockedTanksTransferAndLooseTanksDoNot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitIdleAsync();

        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xformSystem = entMan.System<SharedTransformSystem>();
        var portable = entMan.System<PlumbingPortableSystem>();
        var verbSystem = entMan.System<SharedVerbSystem>();

        EntityUid upperPort = default;
        EntityUid supply = default;
        EntityUid fill = default;
        EntityUid loose = default;
        EntityUid user = default;
        FixedPoint2 looseStart = default;

        await server.WaitPost(() =>
        {
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = 0; x <= 1; x++)
            {
                for (var y = 0; y <= 2; y++)
                {
                    mapSystem.SetTile(map.Grid, new Vector2i(x, y), new Tile(plating));
                }
            }

            EntityCoordinates At(int x, int y) => new(map.Grid, x + 0.5f, y + 0.5f);

            // Port at (0,2) faces south into the duct; the one at (0,0) is turned to face north into it.
            upperPort = entMan.SpawnAtPosition(Port, At(0, 2));
            entMan.SpawnAtPosition(Duct, At(0, 1));
            var lower = entMan.SpawnAtPosition(Port, At(0, 0));
            xformSystem.SetLocalRotation(lower, Angle.FromDegrees(180));

            supply = entMan.SpawnAtPosition(FullTank, At(0, 2));
            fill = entMan.SpawnAtPosition(EmptyTank, At(0, 0));
            loose = entMan.SpawnAtPosition(FullTank, At(1, 0));
            user = entMan.SpawnAtPosition("MobHuman", At(1, 2));

            foreach (var tank in new[] { supply, fill, loose })
            {
                xformSystem.AnchorEntity(tank);
            }

            portable.SetMode(Tank(entMan, fill), PlumbingPortableMode.Fill);
            portable.SetMode(Tank(entMan, loose), PlumbingPortableMode.Supply);
            looseStart = Volume(entMan, loose);
        });

        await pair.RunTicksSync(5);

        // The supply tank's valve is set the way a player would: from the port it sits on.
        await server.WaitPost(() =>
        {
            Assert.That(portable.TryGetDockedTank(upperPort, out var docked) && docked.Owner == supply,
                "the port finds the tank docked on it");

            var verbs = verbSystem.GetLocalVerbs(upperPort, user, typeof(Verb));
            var loc = server.ResolveDependency<ILocalizationManager>();
            var supplyText = loc.GetString("plumbing-portable-verb-set",
                ("mode", loc.GetString("plumbing-portable-mode-supply")));
            var setSupply = verbs.SingleOrDefault(v => v.Text == supplyText);
            Assert.That(setSupply, Is.Not.Null, "the port offers the docked tank's valve");
            verbSystem.ExecuteVerb(setSupply!, user, upperPort);

            Assert.That(entMan.GetComponent<PlumbingPortableComponent>(supply).Mode, Is.EqualTo(PlumbingPortableMode.Supply));
        });

        await pair.RunSeconds(6f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(loose).Anchored, "a tank anchors without a port");
            Assert.That(portable.IsDocked(Tank(entMan, supply)), "the supply tank is docked");
            Assert.That(portable.IsDocked(Tank(entMan, fill)), "the fill tank is docked");
            Assert.That(portable.IsDocked(Tank(entMan, loose)), Is.False, "a tank off a port is not docked");

            // Only docked tanks take part in plumbing updates, and they report their docked valve for the mode light.
            Assert.That(entMan.HasComponent<PlumbingDeviceComponent>(supply), "a docked tank gets plumbing updates");
            Assert.That(entMan.HasComponent<PlumbingDeviceComponent>(loose), Is.False, "a loose tank gets no plumbing updates");
            var appearance = entMan.System<SharedAppearanceSystem>();
            Assert.That(appearance.TryGetData<bool>(supply, PlumbingPortableVisuals.Docked, out var docked) && docked,
                "the docked tank's appearance says it is docked");
            Assert.That(appearance.TryGetData<PlumbingPortableMode>(supply, PlumbingPortableVisuals.Mode, out var shown)
                && shown == PlumbingPortableMode.Supply, "the docked tank's appearance carries its valve setting");

            Assert.That(Volume(entMan, fill), Is.GreaterThan(FixedPoint2.Zero), "the fill tank drew from the supply tank through the duct");
            Assert.That(Volume(entMan, loose), Is.EqualTo(looseStart), "nothing was pulled from the loose tank");
        });

        // Closing the supply valve stops the flow.
        FixedPoint2 closedAt = default;
        await server.WaitPost(() =>
        {
            portable.SetMode(Tank(entMan, supply), PlumbingPortableMode.Closed);
            closedAt = Volume(entMan, fill);
        });

        await pair.RunSeconds(5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(Volume(entMan, fill), Is.EqualTo(closedAt), "a closed valve supplies nothing");
        });

        // Unanchoring undocks the tank.
        await server.WaitPost(() => xformSystem.Unanchor(supply));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(portable.IsDocked(Tank(entMan, supply)), Is.False, "an unanchored tank is undocked");
            Assert.That(portable.TryGetDockedTank(upperPort, out _), Is.False, "the port is empty again");
            Assert.That(entMan.HasComponent<PlumbingDeviceComponent>(supply), Is.False, "an undocked tank stops getting plumbing updates");
        });

        await pair.CleanReturnAsync();
    }
}
