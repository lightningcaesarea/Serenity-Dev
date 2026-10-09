using System.Collections.Generic;
using System.Numerics;
using Content.Server._Serenity.Docks;
using Content.Server.Atmos.EntitySystems;
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
    ///     The dock is a sealed hull: its interior starts and stays at breathable pressure.
    /// </summary>
    [Test]
    public async Task DockIsSealedAndHoldsAir()
    {
        var (pair, console, host) = await Setup(0f);
        var entMan = pair.Server.ResolveDependency<IEntityManager>();
        var docks = entMan.System<SpawnableDockSystem>();
        var atmos = entMan.System<AtmosphereSystem>();

        await pair.Server.WaitAssertion(() => Assert.That(docks.TryRequestDock(console, console), Is.True));
        await pair.RunTicksSync(120);

        await pair.Server.WaitAssertion(() =>
        {
            var dock = OtherGrids(entMan, host)[0];
            foreach (var tile in new[] { new Vector2i(0, 0), new Vector2i(-4, -16), new Vector2i(3, 15) })
            {
                var mixture = atmos.GetTileMixture((dock, null, null), null, tile);
                Assert.That(mixture, Is.Not.Null, $"no air at {tile}");
                Assert.That(mixture!.Pressure, Is.GreaterThan(90f), $"pressure at {tile}");
            }
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
