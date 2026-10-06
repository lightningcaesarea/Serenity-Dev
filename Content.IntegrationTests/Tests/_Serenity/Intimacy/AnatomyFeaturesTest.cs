using System.Collections.Generic;
using System.Linq;
using Content.Server._Serenity.Intimacy;
using Content.Shared._Serenity.Intimacy;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Serenity.Intimacy;

/// <summary>
/// The adult-anatomy markings feed the intimacy engine: wearing a Penis marking exposes the Penis feature,
/// wearing a jumpsuit over it takes PenisAccessible away, and the acts that need those tags are valid.
/// </summary>
[TestFixture]
[TestOf(typeof(IntimacyFeaturesSystem))]
public sealed class AnatomyFeaturesTest
{
    private const string PenisMarking = "GenitalPenisHumanAverage";
    private const string BreastsMarking = "GenitalBreastsMedium";

    [Test]
    public async Task MarkingsAndClothingDriveFeatureTags()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var humanoidSys = entMan.System<SharedHumanoidAppearanceSystem>();
            var inventory = entMan.System<InventorySystem>();

            var human = entMan.Spawn("MobHuman");
            var participant = entMan.GetComponent<IntimacyParticipantComponent>(human);

            Assert.That(participant.Features, Does.Not.Contain("Penis"), "a fresh character has no anatomy markings");

            humanoidSys.AddMarking(human, PenisMarking, false);
            Assert.That(participant.Features, Does.Contain("Penis"));
            Assert.That(participant.Features, Does.Contain("PenisAccessible"), "nothing worn, so it is accessible");
            Assert.That(participant.Features, Does.Not.Contain("Breasts"), "only the markings actually worn count");

            Assert.That(inventory.SpawnItemInSlot(human, "jumpsuit", "ClothingUniformJumpsuitColorGrey", force: true));
            Assert.That(participant.Features, Does.Contain("Penis"), "still has the anatomy under the jumpsuit");
            Assert.That(participant.Features, Does.Not.Contain("PenisAccessible"), "but it is covered");

            Assert.That(inventory.TryUnequip(human, "jumpsuit", force: true));
            Assert.That(participant.Features, Does.Contain("PenisAccessible"), "undressing uncovers it again");

            humanoidSys.AddMarking(human, BreastsMarking, false);
            Assert.That(participant.Features, Does.Contain("Breasts"));
            Assert.That(participant.Features, Does.Contain("BreastsAccessible"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnatomyMarkingsAreOfferedInTheEditor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            var markings = server.ResolveDependency<MarkingManager>();
            var proto = server.ResolveDependency<IPrototypeManager>();

            var categories = new[]
            {
                MarkingCategories.Breasts,
                MarkingCategories.Penis,
                MarkingCategories.Testicles,
                MarkingCategories.Vagina,
                MarkingCategories.Butt,
                MarkingCategories.Belly,
            };

            // Every round-start species, including those whose marking limits are whitelist-only.
            foreach (var species in proto.EnumeratePrototypes<SpeciesPrototype>())
            {
                if (!species.RoundStart)
                    continue;

                foreach (var category in categories)
                {
                    foreach (var sex in new[] { Sex.Male, Sex.Female, Sex.Unsexed })
                    {
                        Assert.That(markings.MarkingsByCategoryAndSpeciesAndSex(category, species.ID, sex), Is.Not.Empty,
                            $"the editor offers {category} to a {sex} {species.ID}");
                    }
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnatomyActsReferenceRealAnatomy()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();

            // Every "<Part>Accessible" / "<Part>" tag an act asks for must be one an anatomy prototype can
            // actually produce, or the act could never appear.
            var produced = new HashSet<string>();
            foreach (var anatomy in proto.EnumeratePrototypes<IntimacyAnatomyPrototype>())
            {
                produced.Add(anatomy.Feature);
                produced.Add(anatomy.AccessibleFeature);
            }

            // Tags produced by other systems (tail markings, sex).
            produced.UnionWith(new[] { "Tail", "Male", "Female" });

            foreach (var act in proto.EnumeratePrototypes<IntimacyActPrototype>())
            {
                foreach (var need in act.ActorNeeds.Concat(act.TargetNeeds))
                {
                    Assert.That(produced, Does.Contain(need), $"act {act.ID} needs unknown feature '{need}'");
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
