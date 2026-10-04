using Content.Server._Serenity.Plumbing;
using Content.Shared._Serenity.Plumbing;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Serenity.Plumbing;

[TestFixture]
public sealed class FluidPortTest
{
    private const string Port = "PlumbingFluidPort";
    private const string FullTank = "WaterTankFull";
    private const string EmptyTank = "WaterTank";

    private static FixedPoint2 Volume(IEntityManager entMan, EntityUid tank)
    {
        Assert.That(entMan.System<SharedSolutionContainerSystem>().TryGetSolution(tank, "tank", out _, out var solution));
        return solution!.Volume;
    }

    /// <summary>
    ///     Two ports facing each other form one network. A tank set to supply on one port feeds a tank set to fill
    ///     on the other; a tank anchored on bare floor stays anchored but never connects.
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

        EntityUid supply = default;
        EntityUid fill = default;
        EntityUid loose = default;
        FixedPoint2 looseStart = default;

        await server.WaitPost(() =>
        {
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = 0; x <= 1; x++)
            {
                for (var y = 0; y <= 1; y++)
                {
                    mapSystem.SetTile(map.Grid, new Vector2i(x, y), new Tile(plating));
                }
            }

            EntityCoordinates At(int x, int y) => new(map.Grid, x + 0.5f, y + 0.5f);

            // Port at (0,1) faces south by default; the one at (0,0) is turned to face north.
            entMan.SpawnAtPosition(Port, At(0, 1));
            var lower = entMan.SpawnAtPosition(Port, At(0, 0));
            xformSystem.SetLocalRotation(lower, Angle.FromDegrees(180));

            supply = entMan.SpawnAtPosition(FullTank, At(0, 1));
            fill = entMan.SpawnAtPosition(EmptyTank, At(0, 0));
            loose = entMan.SpawnAtPosition(FullTank, At(1, 0));

            foreach (var tank in new[] { supply, fill, loose })
            {
                xformSystem.AnchorEntity(tank);
            }

            portable.SetMode((supply, entMan.GetComponent<PlumbingPortableComponent>(supply)), PlumbingPortableMode.Supply);
            portable.SetMode((fill, entMan.GetComponent<PlumbingPortableComponent>(fill)), PlumbingPortableMode.Fill);
            portable.SetMode((loose, entMan.GetComponent<PlumbingPortableComponent>(loose)), PlumbingPortableMode.Supply);
            looseStart = Volume(entMan, loose);
        });

        await pair.RunSeconds(6f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(loose).Anchored, "a tank anchors without a port");
            Assert.That(portable.IsDocked((supply, entMan.GetComponent<PlumbingPortableComponent>(supply))), "the supply tank is docked");
            Assert.That(portable.IsDocked((fill, entMan.GetComponent<PlumbingPortableComponent>(fill))), "the fill tank is docked");
            Assert.That(portable.IsDocked((loose, entMan.GetComponent<PlumbingPortableComponent>(loose))), Is.False, "a tank off a port is not docked");

            Assert.That(Volume(entMan, fill), Is.GreaterThan(FixedPoint2.Zero), "the fill tank drew from the supply tank");
            Assert.That(Volume(entMan, loose), Is.EqualTo(looseStart), "nothing was pulled from the loose tank");
        });

        // Closing the supply valve stops the flow.
        FixedPoint2 closedAt = default;
        await server.WaitPost(() =>
        {
            portable.SetMode((supply, entMan.GetComponent<PlumbingPortableComponent>(supply)), PlumbingPortableMode.Closed);
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
            Assert.That(portable.IsDocked((supply, entMan.GetComponent<PlumbingPortableComponent>(supply))), Is.False, "an unanchored tank is undocked");
        });

        await pair.CleanReturnAsync();
    }
}
