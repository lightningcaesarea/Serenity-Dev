using Content.Server._Serenity.Xenobiology;
using Content.Shared._Serenity.Xenobiology;
using Content.Shared._Starlight.Xenobiology;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Item;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Serenity.Xenobiology;

[TestFixture]
public sealed class SlimeTemperamentTest
{
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private static readonly EntProtoId DocilityPotion = "SlimeDocilityPotion";
    private static readonly ProtoId<ExtractReactionPrototype> PinkPlasmaReaction = "PinkSlimeExtractPlasmaReaction";

    /// <summary>
    /// A slime turns desperate only when starved out, stays desperate until it has eaten back above starving,
    /// may feed on people but not other slimes, stops at the damage cap, and a docile slime never turns.
    /// </summary>
    [Test]
    public async Task StarvedSlimesTurnDesperateWithinLimits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ProtoMan;
        var temperament = entMan.System<SlimeTemperamentSystem>();
        var hunger = entMan.System<HungerSystem>();
        var damageable = entMan.System<DamageableSystem>();
        var mapData = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var slime = entMan.SpawnEntity("XenobiologySlimeGray", mapData.GridCoords);
            var otherSlime = entMan.SpawnEntity("XenobiologySlimeGray", mapData.GridCoords);
            var person = entMan.SpawnEntity("MobHuman", mapData.GridCoords);
            var comp = entMan.GetComponent<SlimeTemperamentComponent>(slime);

            Assert.Multiple(() =>
            {
                // A freshly split slime is starving but not yet desperate.
                hunger.SetHunger(slime, 30f);
                Assert.That(temperament.IsDesperate(slime), Is.False, "a starving slime shouldn't be desperate until it starves out");

                hunger.SetHunger(slime, 0f);
                Assert.That(temperament.IsDesperate(slime), "a starved-out slime should be desperate");

                // One bite doesn't calm it, eating back above starving does.
                hunger.SetHunger(slime, 30f);
                Assert.That(temperament.IsDesperate(slime), "a desperate slime should keep feeding until it is back above starving");
                hunger.SetHunger(slime, 75f);
                Assert.That(temperament.IsDesperate(slime), Is.False, "a slime fed above starving should calm down");

                Assert.That(temperament.IsDesperateTarget(comp, person), "a desperate slime should be able to feed on a person");
                Assert.That(temperament.IsDesperateTarget(comp, otherSlime), Is.False, "slimes shouldn't feed on slimes");

                var hit = new DamageSpecifier(proto.Index(Blunt), comp.DamageCap);
                damageable.TryChangeDamage(person, hit, ignoreResistances: true);
                Assert.That(temperament.IsDesperateTarget(comp, person), Is.False, "a slime should stop feeding on someone at the damage cap");

                // Docile slimes never turn and can be carried.
                temperament.MakeDocile((slime, comp));
                hunger.SetHunger(slime, 0f);
                Assert.That(temperament.IsDesperate(slime), Is.False, "a docile slime should never turn desperate");
                Assert.That(entMan.HasComponent<ItemComponent>(slime), "a docile slime should be carryable");
            });

            entMan.DeleteEntity(slime);
            entMan.DeleteEntity(otherSlime);
            entMan.DeleteEntity(person);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The pink extract's plasma reaction exists and makes the docility potion.
    /// </summary>
    [Test]
    public async Task PinkPlasmaMakesDocilityPotion()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ProtoMan;

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(proto.HasIndex(DocilityPotion));
            Assert.That(proto.HasIndex(PinkPlasmaReaction));
        });

        await pair.CleanReturnAsync();
    }
}
