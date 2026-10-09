using System.Collections.Generic;
using System.Numerics;
using Content.Server._Serenity.Docks;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Shared._Serenity.Docks;
using Content.Shared.Atmos;
using Content.Shared.Interaction;
using Content.Shared._Serenity.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;


namespace Content.IntegrationTests.Tests._Serenity.Docks;

[TestFixture]
public sealed class SpawnableDockTest
{
    private const string Console = "ComputerShipyard";

    private static List<EntityUid> OtherGrids(IEntityManager entMan, EntityUid host)
    {
        var grids = new List<EntityUid>();
        var query = entMan.EntityQueryEnumerator<MapGridComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (uid != host)
                grids.Add(uid);
        }

        return grids;
    }

    private static async Task<(Content.IntegrationTests.Pair.TestPair Pair, EntityUid Console, EntityUid Host)> Setup(float cooldown)
    {
        var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitIdleAsync();

        var entMan = pair.Server.ResolveDependency<IEntityManager>();
        var cfg = pair.Server.ResolveDependency<IConfigurationManager>();

        EntityUid console = default;
        await pair.Server.WaitPost(() =>
        {
            cfg.SetCVar(SerenityCCVars.DocksCooldown, cooldown);
            console = entMan.SpawnAtPosition(Console, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
        });

        return (pair, console, map.Grid.Owner);
    }

    /// <summary>
    ///     With the five minute cooldown in place, a second request straight after the first is refused.
    /// </summary>
    [Test]
    public async Task SecondDockWaitsForTheCooldown()
    {
        var (pair, console, host) = await Setup(300f);
        var entMan = pair.Server.ResolveDependency<IEntityManager>();
        var docks = entMan.System<SpawnableDockSystem>();

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(docks.TryRequestDock(console, console), Is.True);
            Assert.That(docks.TryRequestDock(console, console), Is.False, "second request inside the cooldown");
            Assert.That(OtherGrids(entMan, host), Has.Count.EqualTo(1));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     Docks land 160 tiles from the requesting console's grid and at most three exist.
    /// </summary>
    [Test]
    public async Task DocksSpawnAtDistanceUpToTheLimit()
    {
        var (pair, console, host) = await Setup(0f);
        var entMan = pair.Server.ResolveDependency<IEntityManager>();
        var docks = entMan.System<SpawnableDockSystem>();
        var xforms = entMan.System<SharedTransformSystem>();

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(docks.TryRequestDock(console, console), Is.True);
            Assert.That(docks.TryRequestDock(console, console), Is.True);
            Assert.That(docks.TryRequestDock(console, console), Is.True);
            Assert.That(docks.TryRequestDock(console, console), Is.False, "fourth dock over the limit");

            var spawned = OtherGrids(entMan, host);
            Assert.That(spawned, Has.Count.EqualTo(3));

            var hostOrigin = xforms.GetWorldPosition(host);
            foreach (var dock in spawned)
            {
                var distance = (xforms.GetWorldPosition(dock) - hostOrigin).Length();
                Assert.That(distance, Is.EqualTo(160f).Within(0.5f), "dock distance from the host grid");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The dock is a sealed hull with a working life support plant: every room starts at breathable
    ///     pressure, the RTGs power the APC network, the miner chambers fill, and the vents in the hall
    ///     are fed with a breathable oxygen/nitrogen mix.
    /// </summary>
    [Test]
    public async Task DockIsSealedPoweredAndVented()
    {
        var (pair, console, host) = await Setup(0f);
        var entMan = pair.Server.ResolveDependency<IEntityManager>();
        var docks = entMan.System<SpawnableDockSystem>();
        var atmos = entMan.System<AtmosphereSystem>();
        var nodes = entMan.System<NodeContainerSystem>();

        await pair.Server.WaitAssertion(() => Assert.That(docks.TryRequestDock(console, console), Is.True));
        await pair.RunTicksSync(1500);

        await pair.Server.WaitAssertion(() =>
        {
            var dock = OtherGrids(entMan, host)[0];

            float Pressure(int x, int y)
            {
                var mixture = atmos.GetTileMixture((dock, null, null), null, new Vector2i(x, y));
                Assert.That(mixture, Is.Not.Null, $"no air at {x},{y}");
                return mixture!.Pressure;
            }

            // Hall corners and middle, power room, atmos room: all sealed and breathable.
            foreach (var (x, y) in new[] { (0, 0), (-4, -16), (3, 15), (-3, -22), (3, -18), (-3, 18), (3, 20) })
                Assert.That(Pressure(x, y), Is.GreaterThan(90f), $"pressure at {x},{y}");

            // The miners have filled their sealed chambers.
            Assert.That(Pressure(-3, 27), Is.GreaterThan(150f), "nitrogen chamber");
            Assert.That(Pressure(2, 27), Is.GreaterThan(150f), "oxygen chamber");

            var vents = 0;
            var query = entMan.EntityQueryEnumerator<GasVentPumpComponent, ApcPowerReceiverComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var power, out var xform))
            {
                if (xform.GridUid != dock)
                    continue;

                vents++;
                Assert.That(power.Powered, Is.True, "vent is powered by the dock's APC network");
                Assert.That(nodes.TryGetNode<PipeNode>(uid, "pipe", out var pipe), Is.True);
                Assert.That(pipe!.Air.Pressure, Is.GreaterThan(80f), "supply pipe pressure");
                var oxygen = pipe.Air.GetMoles(Gas.Oxygen) / pipe.Air.TotalMoles;
                var nitrogen = pipe.Air.GetMoles(Gas.Nitrogen) / pipe.Air.TotalMoles;
                Assert.That(oxygen, Is.InRange(0.15f, 0.27f), "oxygen share of the supply");
                Assert.That(nitrogen, Is.InRange(0.73f, 0.85f), "nitrogen share of the supply");
            }

            Assert.That(vents, Is.EqualTo(4));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The hall has two service and two salvage shuttle consoles, two ATMs and a teleporter pad, all
    ///     powered, and the pad is linked to a pad beside the console that requested the dock.
    /// </summary>
    [Test]
    public async Task DockHasConsolesAtmsAndALinkedTeleporter()
    {
        var (pair, console, host) = await Setup(0f);
        var entMan = pair.Server.ResolveDependency<IEntityManager>();
        var docks = entMan.System<SpawnableDockSystem>();
        var maps = entMan.System<SharedMapSystem>();
        var tileDefs = pair.Server.ResolveDependency<ITileDefinitionManager>();

        // The test grid is tiny; give the console some floor to put the host pad on.
        await pair.Server.WaitPost(() =>
        {
            var plating = new Tile(tileDefs["Plating"].TileId);
            for (var x = -3; x <= 3; x++)
            {
                for (var y = -3; y <= 3; y++)
                    maps.SetTile(host, entMan.GetComponent<MapGridComponent>(host), new Vector2i(x, y), plating);
            }
        });

        await pair.Server.WaitAssertion(() => Assert.That(docks.TryRequestDock(console, console), Is.True));
        await pair.RunTicksSync(1500);

        await pair.Server.WaitAssertion(() =>
        {
            var dock = OtherGrids(entMan, host)[0];
            var counts = new Dictionary<string, int>();
            var query = entMan.EntityQueryEnumerator<ApcPowerReceiverComponent, MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out _, out var power, out var meta, out var xform))
            {
                if (xform.GridUid != dock || meta.EntityPrototype is not { } proto)
                    continue;

                if (proto.ID is "ComputerShipyardService" or "ComputerShipyardSalvage" or "SerenityATMFrontier")
                {
                    counts[proto.ID] = counts.GetValueOrDefault(proto.ID) + 1;
                    Assert.That(power.Powered, Is.True, $"{proto.ID} {xform.LocalPosition} is powered");
                }
            }

            Assert.That(counts.GetValueOrDefault("ComputerShipyardService"), Is.EqualTo(2));
            Assert.That(counts.GetValueOrDefault("ComputerShipyardSalvage"), Is.EqualTo(2));
            Assert.That(counts.GetValueOrDefault("SerenityATMFrontier"), Is.EqualTo(2));

            var pads = 0;
            var pad = entMan.EntityQueryEnumerator<DockTeleporterComponent, TransformComponent>();
            while (pad.MoveNext(out _, out var comp, out var xform))
            {
                pads++;
                Assert.That(comp.Partner, Is.Not.Null, "pad is linked");
                var partnerGrid = entMan.GetComponent<TransformComponent>(comp.Partner!.Value).GridUid;
                Assert.That(partnerGrid, Is.Not.EqualTo(xform.GridUid), "partner is on the other grid");
            }

            Assert.That(pads, Is.EqualTo(2), "one pad on the dock and one on the host grid");

            // Using the dock's pad drops the user beside the host pad.
            var xforms = entMan.System<SharedTransformSystem>();
            EntityUid dockPad = default;
            var find = entMan.EntityQueryEnumerator<DockTeleporterComponent, TransformComponent>();
            while (find.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == dock)
                    dockPad = uid;
            }

            var user = entMan.SpawnAtPosition("MobHuman", entMan.GetComponent<TransformComponent>(dockPad).Coordinates);
            entMan.EventBus.RaiseLocalEvent(dockPad, new ActivateInWorldEvent(user, dockPad, true));
            Assert.That(entMan.GetComponent<TransformComponent>(user).GridUid, Is.EqualTo(host), "user arrived on the host grid");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     A dock is never loaded over another grid: with the distance set to zero the only spot to try is
    ///     the host grid itself, so every bearing is blocked and the request fails.
    /// </summary>
    [Test]
    public async Task DocksNeverSpawnInsideAnExistingGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitIdleAsync();

        var entMan = server.ResolveDependency<IEntityManager>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var docks = entMan.System<SpawnableDockSystem>();

        EntityUid console = default;
        await server.WaitPost(() =>
        {
            console = entMan.SpawnAtPosition(Console, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            cfg.SetCVar(SerenityCCVars.DocksDistance, 0f);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(docks.TryRequestDock(console, console), Is.False);
            Assert.That(OtherGrids(entMan, map.Grid.Owner), Is.Empty);
        });

        await pair.CleanReturnAsync();
    }
}
