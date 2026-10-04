using Content.Shared._Serenity.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.SprayPainter.Components;
using Content.Shared.SprayPainter.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Serenity.Chemistry;

[TestFixture]
public sealed class ToggleableSolutionTransferTest
{
    private static readonly EntProtoId[] GenericTanks = ["GenericTank", "GenericTankHighCapacity"];
    private static readonly ProtoId<PaintableGroupPrototype> StorageTanksGroup = "StorageTanks";

    /// <summary>
    /// The generic tank starts in filling mode (containers pour into it) and the toggle swaps it to dispensing
    /// (containers draw from it) and back, always pointing at the tank's own solution.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(GenericTanks))]
    public async Task GenericTankTogglesBetweenFillingAndDispensing(EntProtoId tankId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var toggle = entMan.System<ToggleableSolutionTransferSystem>();
        var solutions = entMan.System<SharedSolutionContainerSystem>();
        var mapData = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var tank = entMan.SpawnEntity(tankId, mapData.GridCoords);
            var comp = entMan.GetComponent<ToggleableSolutionTransferComponent>(tank);

            Assert.Multiple(() =>
            {
                Assert.That(comp.Filling, "the generic tank should start in filling mode");
                Assert.That(entMan.HasComponent<DrainableSolutionComponent>(tank), Is.False, "a filling tank can't be drawn from");
                Assert.That(entMan.GetComponent<RefillableSolutionComponent>(tank).Solution, Is.EqualTo("tank"));
                Assert.That(solutions.TryGetRefillableSolution(tank, out _, out _), "the tank's solution isn't refillable");
            });

            toggle.SetFilling((tank, comp), false);

            Assert.Multiple(() =>
            {
                Assert.That(comp.Filling, Is.False);
                Assert.That(entMan.HasComponent<RefillableSolutionComponent>(tank), Is.False, "a dispensing tank can't be poured into");
                Assert.That(entMan.GetComponent<DrainableSolutionComponent>(tank).Solution, Is.EqualTo("tank"));
                Assert.That(solutions.TryGetDrainableSolution(tank, out _, out _), "the tank's solution isn't drainable");
            });

            toggle.SetFilling((tank, comp), true);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<DrainableSolutionComponent>(tank), Is.False);
                Assert.That(entMan.HasComponent<RefillableSolutionComponent>(tank));
            });

            entMan.DeleteEntity(tank);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Both generic tanks can be spray painted, and every look the painter offers names a body state and fill
    /// settings for the painted tank to copy.
    /// </summary>
    [Test]
    public async Task EveryStorageTankPaintStyleHasTankVisuals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var group = proto.Index(StorageTanksGroup);

            Assert.Multiple(() =>
            {
                Assert.That(group.Styles, Contains.Key(group.DefaultStyle));

                foreach (var (style, entity) in group.Styles)
                {
                    var entityProto = proto.Index(entity);
                    Assert.That(entityProto.TryGetComponent<PaintableTankVisualsComponent>(out var visuals, factory),
                        $"{style} ({entity}) has no PaintableTankVisuals");
                    Assert.That(visuals?.BaseState, Is.Not.Empty, $"{style} ({entity}) has no base state");
                    Assert.That(entityProto.TryGetComponent<SolutionContainerVisualsComponent>(out var fill, factory)
                        && fill.FillBaseName != null && fill.MaxFillLevels > 0,
                        $"{style} ({entity}) has no fill levels to copy");
                }

                foreach (var tank in GenericTanks)
                {
                    Assert.That(proto.Index(tank).TryGetComponent<PaintableComponent>(out var paintable, factory)
                        && paintable.Group == StorageTanksGroup,
                        $"{tank} can't be spray painted as a storage tank");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
