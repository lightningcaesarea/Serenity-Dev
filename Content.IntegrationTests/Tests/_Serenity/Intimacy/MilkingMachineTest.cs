using Content.Server._Serenity.Intimacy;
using Content.Shared._Serenity.Consent;
using Content.Shared._Serenity.Intimacy;
using Content.Shared.Buckle;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Serenity.Intimacy;

/// <summary>
/// The milking machine collects fluids for the anatomy its occupant has, only with consent,
/// and adds a burst on climax.
/// </summary>
[TestFixture]
[TestOf(typeof(MilkingMachineSystem))]
public sealed class MilkingMachineTest
{
    [Test]
    public async Task CollectsFromConsentingOccupant()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entMan = server.ResolveDependency<IEntityManager>();
        var buckle = entMan.System<SharedBuckleSystem>();
        var humanoid = entMan.System<SharedHumanoidAppearanceSystem>();
        var machines = entMan.System<MilkingMachineSystem>();
        var solutions = entMan.System<SharedSolutionContainerSystem>();
        var intimacy = entMan.System<SharedIntimacySystem>();
        var climax = entMan.System<ClimaxSystem>();

        EntityUid machine = default;
        EntityUid occupant = default;
        EntityUid other = default;

        await server.WaitAssertion(() =>
        {
            machine = entMan.SpawnEntity("MilkingMachine", testMap.GridCoords);
            occupant = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            other = entMan.SpawnEntity("MobHuman", testMap.GridCoords);

            humanoid.AddMarking(occupant, "GenitalBreastsMedium", false);
            humanoid.AddMarking(occupant, "GenitalPenisHumanAverage", false);
            Assert.That(buckle.TryBuckle(occupant, occupant, machine));

            var comp = entMan.GetComponent<MilkingMachineComponent>(machine);

            // No consent snapshot yet: someone else can't switch it on, and the occupant's own switch does nothing.
            Assert.That(machines.TrySetMode((machine, comp), MilkingMachineMode.High, other), Is.False);
            Assert.That(machines.TrySetMode((machine, comp), MilkingMachineMode.High, occupant));
        });

        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            Assert.That(solutions.TryGetSolution(machine, "tank", out _, out var tank));
            Assert.That(tank!.Volume, Is.EqualTo(FixedPoint2.Zero), "nothing is collected without the lewd interactions consent");

            var consent = entMan.EnsureComponent<PlayerConsentComponent>(occupant);
            consent.Allowed.Add(SharedIntimacySystem.MasterToggle);
        });

        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            Assert.That(solutions.TryGetSolution(machine, "tank", out _, out var tank));
            Assert.That(tank!.GetTotalPrototypeQuantity("Milk"), Is.GreaterThan(FixedPoint2.Zero), "breasts give milk");
            Assert.That(intimacy.GetStat(occupant, "Arousal"), Is.GreaterThan(0f), "the pump arouses the occupant");

            var semenBefore = tank.GetTotalPrototypeQuantity("Semen");
            Assert.That(intimacy.TrySetStat(occupant, "Pleasure", 100f));
            Assert.That(climax.TryClimax(occupant, false));
            Assert.That(tank.GetTotalPrototypeQuantity("Semen") - semenBefore, Is.GreaterThanOrEqualTo(FixedPoint2.New(10)),
                "a climax while strapped in adds a burst");
            Assert.That(tank.GetTotalPrototypeQuantity("VaginalFluid"), Is.EqualTo(FixedPoint2.Zero), "only anatomy the occupant has");

            var comp = entMan.GetComponent<MilkingMachineComponent>(machine);
            Assert.That(machines.TrySetMode((machine, comp), MilkingMachineMode.Low, other), Is.False,
                "others still need the container consent toggle");
            entMan.GetComponent<PlayerConsentComponent>(occupant).Allowed.Add(comp.OthersToggle);
            Assert.That(machines.TrySetMode((machine, comp), MilkingMachineMode.Low, other));

            Assert.That(buckle.TryUnbuckle(occupant, occupant));
            Assert.That(comp.Mode, Is.EqualTo(MilkingMachineMode.Off), "standing up switches it off");
        });

        await pair.CleanReturnAsync();
    }
}
