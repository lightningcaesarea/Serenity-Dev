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
public sealed class OneWayValveTest
{
    private const string Port = "PlumbingFluidPort";
    private const string Valve = "PlumbingOneWayValve";
    private const string FullTank = "WaterTankFull";
    private const string EmptyTank = "WaterTank";

    private static FixedPoint2 Volume(IEntityManager entMan, EntityUid tank)
    {
        Assert.That(entMan.System<SharedSolutionContainerSystem>().TryGetSolution(tank, "tank", out _, out var solution));
        return solution!.Volume;
    }

    /// <summary>
    ///     Two columns of supply port, valve, fill port. The valve's arrow points down in the left column,
    ///     so liquid reaches the bottom tank; it points up in the right column, so nothing gets through.
    /// </summary>
    [Test]
    public async Task ValveOnlyPassesInArrowDirection()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitIdleAsync();

        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xformSystem = entMan.System<SharedTransformSystem>();
        var portable = entMan.System<PlumbingPortableSystem>();

        EntityUid forwardFill = default;
        EntityUid reverseFill = default;

        await server.WaitPost(() =>
        {
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = 0; x <= 2; x++)
            {
                for (var y = 0; y <= 2; y++)
                {
                    mapSystem.SetTile(map.Grid, new Vector2i(x, y), new Tile(plating));
                }
            }

            EntityCoordinates At(int x, int y) => new(map.Grid, x + 0.5f, y + 0.5f);

            EntityUid Column(int x, bool valveForward)
            {
                // Top port faces south into the valve; bottom port is turned to face north into it.
                entMan.SpawnAtPosition(Port, At(x, 2));
                var bottom = entMan.SpawnAtPosition(Port, At(x, 0));
                xformSystem.SetLocalRotation(bottom, Angle.FromDegrees(180));

                // Unrotated, the valve takes in from the north and gives out to the south.
                var valve = entMan.SpawnAtPosition(Valve, At(x, 1));
                if (!valveForward)
                    xformSystem.SetLocalRotation(valve, Angle.FromDegrees(180));

                var supply = entMan.SpawnAtPosition(FullTank, At(x, 2));
                var fill = entMan.SpawnAtPosition(EmptyTank, At(x, 0));
                xformSystem.AnchorEntity(supply);
                xformSystem.AnchorEntity(fill);

                portable.SetMode((supply, entMan.GetComponent<PlumbingPortableComponent>(supply)), PlumbingPortableMode.Supply);
                portable.SetMode((fill, entMan.GetComponent<PlumbingPortableComponent>(fill)), PlumbingPortableMode.Fill);
                return fill;
            }

            forwardFill = Column(0, valveForward: true);
            reverseFill = Column(2, valveForward: false);
        });

        await pair.RunSeconds(8f);

        await server.WaitAssertion(() =>
        {
            Assert.That(Volume(entMan, forwardFill), Is.GreaterThan(FixedPoint2.Zero), "liquid passes in the arrow's direction");
            Assert.That(Volume(entMan, reverseFill), Is.EqualTo(FixedPoint2.Zero), "nothing passes against the arrow");
        });

        await pair.CleanReturnAsync();
    }
}
