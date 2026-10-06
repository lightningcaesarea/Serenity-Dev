using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Serenity.SectorEvents;

/// <summary>
/// The stock map tests only walk /Maps/_Starlight, so the Serenity POI and sector event grids get their own load check.
/// Loading runs MapInit on the grid, and any unknown prototype or component logs an error, which fails the test.
/// </summary>
[TestFixture]
public sealed class SectorMapsLoadTest
{
    [Test]
    [TestCase("/Maps/_Serenity/POI/cove.yml")]
    [TestCase("/Maps/_Serenity/Events/cache.yml")]
    [TestCase("/Maps/_Serenity/Events/vault.yml")]
    [TestCase("/Maps/_Serenity/Events/vaultsmall.yml")]
    [TestCase("/Maps/_Serenity/Events/syndieftlintercept.yml")]
    public async Task GridLoads(string path)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitIdleAsync();

        var entMan = server.ResolveDependency<IEntityManager>();
        var loader = entMan.System<MapLoaderSystem>();
        var loaded = false;

        await server.WaitPost(() =>
        {
            loaded = loader.TryLoadGrid(map.MapId, new ResPath(path), out var grid);
            Assert.That(grid, Is.Not.Null);
        });

        Assert.That(loaded, $"{path} should load");
        await pair.RunTicksSync(10);
        await pair.CleanReturnAsync();
    }
}
