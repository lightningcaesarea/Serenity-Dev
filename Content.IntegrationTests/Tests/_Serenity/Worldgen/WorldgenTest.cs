using System.Linq;
using Content.Server._Serenity.Worldgen;
using Content.Server.Worldgen.Components.Debris;
using Content.Server.Worldgen.Systems.Debris;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._Serenity.Worldgen;

[TestFixture]
public sealed class WorldgenTest
{
    /// <summary>
    ///     Debris is cancelled inside a grid's exclusion radius and allowed outside it.
    /// </summary>
    [Test]
    public async Task ExclusionZoneCancelsNearbyDebris()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitIdleAsync();

        var entMan = server.ResolveDependency<IEntityManager>();
        bool near = false, far = true;

        await server.WaitPost(() =>
        {
            var zone = entMan.EnsureComponent<WorldgenExclusionZoneComponent>(map.Grid);
            zone.Radius = 300f;

            var controller = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.AddComponent<DebrisFeaturePlacerControllerComponent>(controller);

            var nearEv = new PrePlaceDebrisFeatureEvent(new EntityCoordinates(map.MapUid, 100, 0), controller);
            entMan.EventBus.RaiseLocalEvent(controller, ref nearEv);
            near = nearEv.Handled;

            var farEv = new PrePlaceDebrisFeatureEvent(new EntityCoordinates(map.MapUid, 1000, 0), controller);
            entMan.EventBus.RaiseLocalEvent(controller, ref farEv);
            far = farEv.Handled;
        });

        Assert.That(near, Is.True, "debris 100 tiles from the grid should be cancelled");
        Assert.That(far, Is.False, "debris 1000 tiles from the grid should be allowed");

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     A wreck dungeon debris grid fills itself with tiles after spawning.
    /// </summary>
    [Test]
    public async Task DungeonDebrisGeneratesTiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitIdleAsync();

        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        EntityUid debris = default;

        await server.WaitPost(() =>
        {
            debris = entMan.SpawnEntity("DungeonDebrisChunkDebrisSmall", new EntityCoordinates(map.MapUid, 500, 500));
        });

        var tiles = 0;
        for (var i = 0; i < 20 && tiles == 0; i++)
        {
            await pair.RunTicksSync(30);
            await server.WaitPost(() =>
            {
                tiles = mapSystem.GetAllTiles(debris, entMan.GetComponent<MapGridComponent>(debris)).Count();
            });
        }

        Assert.That(tiles, Is.GreaterThan(0), "the dungeon should have generated floor on the debris grid");

        await pair.CleanReturnAsync();
    }
}
