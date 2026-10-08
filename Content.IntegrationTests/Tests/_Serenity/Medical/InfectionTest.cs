using Content.Shared.Damage.Systems;
using System.Linq;
using Content.Server._Serenity.Medical.Sterility;
using Content.Server._Serenity.Medical.Wounds;
using Content.Shared._Serenity.Medical.Sterility;
using Content.Shared._Serenity.Medical.Wounds;
using Content.Shared._Serenity.Medical.Wounds.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityConditions.Conditions;
using Content.Shared.EntityEffects.Effects.Damage;
using Content.Shared.EntityEffects.Effects.StatusEffects;
using Content.Shared.Damage.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Serenity.Medical;

[TestFixture]
[TestOf(typeof(InfectionSystem))]
public sealed class InfectionTest
{
    private static readonly ProtoId<WoundTypePrototype> InfectionWound = "Infection";
    private static readonly ProtoId<ReagentPrototype> Antibiox = "Antibiox";

    private static float Poison(IEntityManager entMan, EntityUid uid)
    {
        return entMan.System<DamageableSystem>().GetAllDamage(uid).DamageDict.TryGetValue("Poison", out var poison) ? poison.Float() : 0f;
    }

    private static WoundEntry Infection(WoundComponent comp)
    {
        return comp.ActiveWounds.FirstOrDefault(w => w.WoundTypeId == InfectionWound);
    }

    /// <summary>
    /// An untreated infection gets worse on its timer and poisons the patient, and natural wound decay leaves it alone.
    /// </summary>
    [Test]
    public async Task InfectionWorsensAndDoesNotDecay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapData = await pair.CreateTestMap();
        var infections = entMan.System<InfectionSystem>();
        var regen = entMan.System<WoundRegenSystem>();
        var config = server.ProtoMan.Index<SterilityConfigPrototype>(SterilityConfigPrototype.DefaultId);
        var now = server.ResolveDependency<IGameTiming>().CurTime;

        await server.WaitAssertion(() =>
        {
            var patient = entMan.SpawnEntity("MobHuman", mapData.GridCoords);
            var comp = entMan.GetComponent<WoundComponent>(patient);

            Assert.That(infections.TryInfect(patient, comp), Is.True);
            Assert.That(infections.TryInfect(patient, comp), Is.False, "one infection at a time");
            Assert.That(Infection(comp)!.Tier, Is.EqualTo(1));

            // Natural decay must not touch it, even when its timer is long past
            Infection(comp)!.NextDecayTime = TimeSpan.Zero;
            regen.DecayWounds(patient, comp);
            Assert.That(Infection(comp)?.Tier, Is.EqualTo(1), "infections don't heal by themselves");
            Infection(comp)!.NextDecayTime = now + TimeSpan.FromSeconds(config.EscalationSeconds[0]);

            // Not due yet: nothing changes and tier 1 is painless
            infections.Tick(patient, comp, now);
            Assert.That(Infection(comp)!.Tier, Is.EqualTo(1));
            Assert.That(Poison(entMan, patient), Is.EqualTo(0f));

            // Due: tier 2, which poisons
            now += TimeSpan.FromSeconds(config.EscalationSeconds[0] + 1);
            infections.Tick(patient, comp, now);
            Assert.That(Infection(comp)!.Tier, Is.EqualTo(2));
            Assert.That(Poison(entMan, patient), Is.GreaterThan(0f), "a spreading infection poisons the patient");

            // Then tier 3, and it stays there
            now += TimeSpan.FromSeconds(config.EscalationSeconds[1] + 1);
            infections.Tick(patient, comp, now);
            Assert.That(Infection(comp)!.Tier, Is.EqualTo(3));

            now += TimeSpan.FromHours(1);
            infections.Tick(patient, comp, now);
            Assert.That(Infection(comp)!.Tier, Is.EqualTo(3), "tier 3 is the worst");

            entMan.DeleteEntity(patient);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A broad-spectrum antibiotic freezes an infection where it is (no worsening, no symptoms) and blocks new ones,
    /// but doesn't cure it: once it wears off the infection carries on.
    /// </summary>
    [Test]
    public async Task AntibioticsFreezeInfectionButDoNotCureIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapData = await pair.CreateTestMap();
        var infections = entMan.System<InfectionSystem>();
        var effects = entMan.System<StatusEffectsSystem>();
        var wounds = entMan.System<SharedWoundSystem>();
        var config = server.ProtoMan.Index<SterilityConfigPrototype>(SterilityConfigPrototype.DefaultId);
        var now = server.ResolveDependency<IGameTiming>().CurTime;

        EntityUid patient = default;
        WoundComponent comp = default!;

        await server.WaitAssertion(() =>
        {
            patient = entMan.SpawnEntity("MobHuman", mapData.GridCoords);
            comp = entMan.GetComponent<WoundComponent>(patient);

            infections.TryInfect(patient, comp);
            Infection(comp).Tier = 2;
            Infection(comp).NextDecayTime = now;

            Assert.That(effects.TryAddStatusEffectDuration(patient, InfectionSystem.AntibioticEffect, TimeSpan.FromHours(1)));
            Assert.That(infections.HasAntibiotic(patient));

            // Even with its timer long past, and however much time goes by, it doesn't get worse or hurt
            for (var i = 0; i < 20; i++)
            {
                now += TimeSpan.FromSeconds(config.EscalationSeconds[1] + 1);
                infections.Tick(patient, comp, now);
            }

            Assert.That(Infection(comp).Tier, Is.EqualTo(2), "frozen at its current tier");
            Assert.That(Poison(entMan, patient), Is.EqualTo(0f), "symptoms are suppressed");

            // Protected against new ones while it lasts
            wounds.AddWound(patient, comp, new WoundEntry("SlashLaceration", 3));
            Assert.That(infections.OpenWoundInfectionChance(patient, comp), Is.EqualTo(0f));

            // The drug wears off (status effects are deleted on the next tick)
            Assert.That(effects.TryRemoveStatusEffect(patient, InfectionSystem.AntibioticEffect));
        });

        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(infections.HasAntibiotic(patient), Is.False);
            Assert.That(Infection(comp), Is.Not.Null, "an antibiotic doesn't cure it");

            // The timer was held at a full delay, so it resumes from there rather than worsening at once...
            infections.Tick(patient, comp, now);
            Assert.That(Infection(comp).Tier, Is.EqualTo(2));

            // ...and worsens again once that delay has passed
            now += TimeSpan.FromSeconds(config.EscalationSeconds[1] + 1);
            infections.Tick(patient, comp, now);
            Assert.That(Infection(comp).Tier, Is.EqualTo(3));
            Assert.That(Poison(entMan, patient), Is.GreaterThan(0f), "symptoms return");

            entMan.DeleteEntity(patient);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Overdosing on an antibiotic cancels its protection (no freeze, no symptom suppression, new infections allowed)
    /// and makes an infection escalate faster than it would untreated.
    /// </summary>
    [Test]
    public async Task AntibioticOverdoseSpeedsUpInfection()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapData = await pair.CreateTestMap();
        var infections = entMan.System<InfectionSystem>();
        var effects = entMan.System<StatusEffectsSystem>();
        var wounds = entMan.System<SharedWoundSystem>();
        var config = server.ProtoMan.Index<SterilityConfigPrototype>(SterilityConfigPrototype.DefaultId);
        var start = server.ResolveDependency<IGameTiming>().CurTime;

        await server.WaitAssertion(() =>
        {
            var patient = entMan.SpawnEntity("MobHuman", mapData.GridCoords);
            var comp = entMan.GetComponent<WoundComponent>(patient);

            Assert.That(effects.TryAddStatusEffectDuration(patient, InfectionSystem.AntibioticEffect, TimeSpan.FromHours(1)));
            Assert.That(infections.HasAntibiotic(patient), "a plain dose protects");

            Assert.That(effects.TryAddStatusEffectDuration(patient, InfectionSystem.AntibioticOverdoseEffect, TimeSpan.FromHours(1)));
            Assert.That(infections.IsOverdosed(patient));
            Assert.That(infections.HasAntibiotic(patient), Is.False, "an overdose cancels the protection");

            // New infections are no longer blocked
            wounds.AddWound(patient, comp, new WoundEntry("SlashLaceration", 3));
            Assert.That(infections.OpenWoundInfectionChance(patient, comp), Is.GreaterThan(0f));

            // An existing infection is not frozen: it worsens, and well before its normal delay is up
            infections.TryInfect(patient, comp);
            var now = start;
            Infection(comp).NextDecayTime = now + TimeSpan.FromSeconds(config.EscalationSeconds[0]);

            var elapsed = 0f;
            while (Infection(comp).Tier == 1 && elapsed < config.EscalationSeconds[0])
            {
                now += TimeSpan.FromSeconds(config.InfectionTickSeconds);
                elapsed += config.InfectionTickSeconds;
                infections.Tick(patient, comp, now);
            }

            Assert.That(Infection(comp).Tier, Is.EqualTo(2), "the infection escalated");
            Assert.That(elapsed, Is.LessThan(config.EscalationSeconds[0] / 2f), "and much faster than untreated");

            entMan.DeleteEntity(patient);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Untreated open wounds carry an infection risk that scales with tier; minor wounds, other kinds of wound, an
    /// existing infection and an antibiotic all mean no risk.
    /// </summary>
    [Test]
    public async Task OpenWoundsCarryInfectionRisk()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapData = await pair.CreateTestMap();
        var infections = entMan.System<InfectionSystem>();
        var effects = entMan.System<StatusEffectsSystem>();
        var wounds = entMan.System<SharedWoundSystem>();

        await server.WaitAssertion(() =>
        {
            var patient = entMan.SpawnEntity("MobHuman", mapData.GridCoords);
            var comp = entMan.GetComponent<WoundComponent>(patient);

            Assert.That(infections.OpenWoundInfectionChance(patient, comp), Is.EqualTo(0f), "no wounds, no risk");

            wounds.AddWound(patient, comp, new WoundEntry("BluntFracture", 3));
            Assert.That(infections.OpenWoundInfectionChance(patient, comp), Is.EqualTo(0f), "a closed fracture doesn't get infected");

            wounds.AddWound(patient, comp, new WoundEntry("SlashLaceration", 1));
            Assert.That(infections.OpenWoundInfectionChance(patient, comp), Is.EqualTo(0f), "a scratch is too minor");

            wounds.AddWound(patient, comp, new WoundEntry("SlashLaceration", 2));
            var minor = infections.OpenWoundInfectionChance(patient, comp);
            Assert.That(minor, Is.GreaterThan(0f));

            wounds.AddWound(patient, comp, new WoundEntry("PiercingPuncture", 3));
            Assert.That(infections.OpenWoundInfectionChance(patient, comp), Is.GreaterThan(minor), "more and worse wounds, more risk");

            Assert.That(effects.TryAddStatusEffectDuration(patient, InfectionSystem.AntibioticEffect, TimeSpan.FromMinutes(1)));
            Assert.That(infections.OpenWoundInfectionChance(patient, comp), Is.EqualTo(0f), "an antibiotic protects");

            entMan.DeleteEntity(patient);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A dirty operation can infect the patient; a clean one can't, and neither can one under an antibiotic.
    /// </summary>
    [Test]
    public async Task DirtySurgeryCanInfect()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapData = await pair.CreateTestMap();
        var effects = entMan.System<StatusEffectsSystem>();
        var wounds = entMan.System<SharedWoundSystem>();
        var config = server.ProtoMan.Index<SterilityConfigPrototype>(SterilityConfigPrototype.DefaultId);

        Assert.Multiple(() =>
        {
            Assert.That(config.SurgeryInfectionChance(config.SurgeryInfectionThreshold), Is.EqualTo(0f));
            Assert.That(config.SurgeryInfectionChance(config.SurgeryInfectionThreshold + 10f), Is.GreaterThan(0f));
            Assert.That(config.SurgeryInfectionChance(10000f), Is.EqualTo(config.SurgeryInfectionMaxChance), "capped");
        });

        await server.WaitAssertion(() =>
        {
            var surgeon = entMan.SpawnEntity("MobHuman", mapData.GridCoords);
            var clean = entMan.SpawnEntity("MobHuman", mapData.GridCoords);
            var dirty = entMan.SpawnEntity("MobHuman", mapData.GridCoords);
            var protectedPatient = entMan.SpawnEntity("MobHuman", mapData.GridCoords);
            Assert.That(effects.TryAddStatusEffectDuration(protectedPatient, InfectionSystem.AntibioticEffect, TimeSpan.FromHours(1)));

            // Raise the event many times so the odds of a chance-based miss are negligible
            for (var i = 0; i < 40; i++)
            {
                var low = new SurgeryStepDirtiedEvent(surgeon, config.SurgeryInfectionThreshold, 0f);
                entMan.EventBus.RaiseLocalEvent(clean, ref low);

                var high = new SurgeryStepDirtiedEvent(surgeon, 1000f, 5f);
                entMan.EventBus.RaiseLocalEvent(dirty, ref high);
                entMan.EventBus.RaiseLocalEvent(protectedPatient, ref high);
            }

            Assert.That(Infection(entMan.GetComponent<WoundComponent>(clean)), Is.Null, "a clean operation can't infect");
            Assert.That(Infection(entMan.GetComponent<WoundComponent>(dirty)), Is.Not.Null, "a very dirty operation infects");
            Assert.That(Infection(entMan.GetComponent<WoundComponent>(protectedPatient)), Is.Null, "an antibiotic protects");

            foreach (var uid in new[] { surgeon, clean, dirty, protectedPatient })
            {
                entMan.DeleteEntity(uid);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The infection config refers to real things and its arrays match the tier count.
    /// </summary>
    [Test]
    public async Task InfectionConfigIsConsistent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ProtoMan;

        Assert.Multiple(() =>
        {
            foreach (var config in proto.EnumeratePrototypes<SterilityConfigPrototype>())
            {
                Assert.That(config.EscalationSeconds, Has.Length.EqualTo(WoundsConstants.MaxWoundTier), $"{config.ID} escalation times");
                Assert.That(config.SymptomDamage, Has.Length.EqualTo(WoundsConstants.MaxWoundTier), $"{config.ID} symptom damage");
                Assert.That(config.EscalationSeconds.Take(WoundsConstants.MaxWoundTier - 1), Has.All.GreaterThan(0f), $"{config.ID} escalation times must be positive");
                Assert.That(config.SymptomDamage, Has.All.GreaterThanOrEqualTo(0f));
                Assert.That(config.InfectionTickSeconds, Is.GreaterThan(0f));

                Assert.That(proto.TryIndex(config.InfectionWound, out var wound), $"{config.ID} infection wound {config.InfectionWound} doesn't exist");
                Assert.That(wound?.Category.Id, Is.EqualTo(config.InfectionCategory.Id), $"{config.ID} infection wound must be in the infection category");

                Assert.That(proto.TryIndex(config.InfectionCategory, out var category));
                Assert.That(category?.Decays, Is.False, "infections must not heal by themselves");

                foreach (var (id, risk) in config.OpenWounds)
                {
                    Assert.That(proto.TryIndex(id, out var riskCategory), $"{config.ID} open-wound risk for unknown category {id}");
                    Assert.That(riskCategory?.Derived, Is.False, $"{config.ID} {id} is derived, so it has no wounds to infect");
                    Assert.That(risk.MinTier, Is.InRange(1, WoundsConstants.MaxWoundTier));
                    Assert.That(risk.ChancePerTier, Is.InRange(0f, 1f));
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A single 15u dose of Antibiox (what an auto-injector holds) protects for about seven minutes: how long it
    /// takes to metabolize plus how long the antibiotic effect lingers afterwards. The dysbiosis debuff outlasts it.
    /// </summary>
    [Test]
    public async Task FifteenUnitsOfAntibioxProtectForAboutSevenMinutes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ProtoMan;
        var reagent = proto.Index(Antibiox);

        Assert.That(reagent.Metabolisms, Is.Not.Null);
        var entry = reagent.Metabolisms!.Metabolisms.Single(m => m.Key.Id == "Bloodstream").Value;

        TimeSpan? Effect(string effectProto) => entry.Effects
            .OfType<ModifyStatusEffect>()
            .Single(e => e.EffectProto == effectProto).Time;

        // Metabolizing runs once a second and removes the rate's worth each time
        const float dose = 15f;
        var inBlood = dose / entry.MetabolismRate.Float();
        var antibiotic = inBlood + Effect(InfectionSystem.AntibioticEffect)!.Value.TotalSeconds;
        var dysbiosis = inBlood + Effect("StatusEffectDysbiosis")!.Value.TotalSeconds;

        Assert.Multiple(() =>
        {
            Assert.That(antibiotic, Is.EqualTo(7 * 60).Within(15), "15u should protect for about 7 minutes");
            Assert.That(dysbiosis, Is.GreaterThan(antibiotic), "the dysbiosis debuff outlasts the protection");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The antibiox auto-injector holds exactly one 15u dose: full, with no room for more.
    /// </summary>
    [Test]
    public async Task AntibioxInjectorHoldsOneDose()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapData = await pair.CreateTestMap();
        var solutions = entMan.System<SharedSolutionContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var injector = entMan.SpawnEntity("AntibioxAutoInjector", mapData.GridCoords);

            Assert.That(solutions.TryGetSolution(injector, "hypospray", out _, out var solution));
            Assert.That(solution!.MaxVolume.Float(), Is.EqualTo(15f), "capacity is one dose");
            Assert.That(solution.Volume.Float(), Is.EqualTo(15f), "and it comes full");
            Assert.That(solution.GetTotalPrototypeQuantity(Antibiox).Float(), Is.EqualTo(15f));

            entMan.DeleteEntity(injector);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// An Antibiox overdose hurts but doesn't kill: a double dose (two auto-injectors, 30u) deals roughly 60 Poison in
    /// all while it stays at or over the overdose line, well short of critical.
    /// </summary>
    [Test]
    public async Task DoubleDoseOfAntibioxHurtsButDoesNotKill()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ProtoMan;
        var reagent = proto.Index(Antibiox);

        Assert.That(reagent.Metabolisms, Is.Not.Null);
        var entry = reagent.Metabolisms!.Metabolisms.Single(m => m.Key.Id == "Bloodstream").Value;
        var overdose = entry.Effects.OfType<HealthChange>().Single();
        var threshold = overdose.Conditions!.OfType<ReagentCondition>().Single(c => c.Reagent == Antibiox).Min.Float();
        var poisonPerTick = overdose.Damage.DamageDict["Poison"].Float();

        // Metabolizing runs once a second and removes the rate's worth each time; the overdose effect applies on every
        // tick that starts at or over the threshold
        const float doubleDose = 30f;
        var overdosedTicks = MathF.Floor((doubleDose - threshold) / entry.MetabolismRate.Float()) + 1;
        var total = overdosedTicks * poisonPerTick;

        Assert.Multiple(() =>
        {
            Assert.That(threshold, Is.GreaterThan(15f), "a single 15u auto-injector must not overdose");
            Assert.That(total, Is.EqualTo(60f).Within(5f), "a double dose should deal about 60 Poison in all");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// An antibiotic overdose makes a new infection much likelier, from open wounds and from dirty surgery alike,
    /// without lifting the caps.
    /// </summary>
    /// <summary>
    /// The admin <c>infect</c> command (and the <see cref="InfectionSystem.SetInfection"/> it calls) gives a mob an
    /// infection at the tier asked for, or moves the one it has, and refuses a mob that can't be infected or a bad tier.
    /// </summary>
    [Test]
    public async Task InfectCommandSetsTheTier()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapData = await pair.CreateTestMap();
        var infections = entMan.System<InfectionSystem>();
        var host = server.ResolveDependency<IConsoleHost>();

        await server.WaitAssertion(() =>
        {
            var patient = entMan.SpawnEntity("MobHuman", mapData.GridCoords);
            var comp = entMan.GetComponent<WoundComponent>(patient);
            var id = entMan.GetNetEntity(patient);

            // Straight to tier 2 on a healthy mob, then moved to 3 and back to 1: always one infection.
            host.ExecuteCommand(null, $"infect {id} 2");
            Assert.That(Infection(comp)!.Tier, Is.EqualTo(2));
            Assert.That(comp.ActiveWounds.Count(w => w.WoundTypeId == InfectionWound), Is.EqualTo(1));

            host.ExecuteCommand(null, $"infect {id} 3");
            Assert.That(Infection(comp)!.Tier, Is.EqualTo(3));
            Assert.That(Infection(comp)!.NextDecayTime, Is.EqualTo(TimeSpan.MaxValue), "the last tier doesn't escalate");

            host.ExecuteCommand(null, $"infect {id} 1");
            Assert.That(Infection(comp)!.Tier, Is.EqualTo(1));
            Assert.That(Infection(comp)!.NextDecayTime, Is.Not.EqualTo(TimeSpan.MaxValue), "a lower tier escalates again");
            Assert.That(comp.ActiveWounds.Count(w => w.WoundTypeId == InfectionWound), Is.EqualTo(1));

            // The command refuses a tier outside 1-3 (the test console fails on any error line, so only the method is
            // exercised here); the method clamps instead.
            Assert.That(infections.SetInfection(patient, comp, 99), Is.EqualTo(WoundsConstants.MaxWoundTier));
            Assert.That(Infection(comp)!.Tier, Is.EqualTo(WoundsConstants.MaxWoundTier));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AntibioticOverdoseRaisesInfectionChance()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapData = await pair.CreateTestMap();
        var infections = entMan.System<InfectionSystem>();
        var effects = entMan.System<StatusEffectsSystem>();
        var wounds = entMan.System<SharedWoundSystem>();
        var config = server.ProtoMan.Index<SterilityConfigPrototype>(SterilityConfigPrototype.DefaultId);
        var multiplier = config.OverdoseInfectionChanceMultiplier;

        Assert.That(multiplier, Is.GreaterThan(1f));

        // Dirty surgery: multiplied, still capped
        var dirt = config.SurgeryInfectionThreshold + 10f;
        Assert.Multiple(() =>
        {
            Assert.That(config.SurgeryInfectionChance(dirt, overdosed: true),
                Is.EqualTo(Math.Min(config.SurgeryInfectionChance(dirt) * multiplier, config.SurgeryInfectionMaxChance)).Within(0.0001f));
            Assert.That(config.SurgeryInfectionChance(10000f, overdosed: true), Is.EqualTo(config.SurgeryInfectionMaxChance), "still capped");
        });

        await server.WaitAssertion(() =>
        {
            var patient = entMan.SpawnEntity("MobHuman", mapData.GridCoords);
            var comp = entMan.GetComponent<WoundComponent>(patient);

            wounds.AddWound(patient, comp, new WoundEntry("SlashLaceration", 2));
            var normal = infections.OpenWoundInfectionChance(patient, comp);
            Assert.That(normal, Is.GreaterThan(0f));

            // Overdosed: on the antibiotic, but its protection is gone and the risk is multiplied
            Assert.That(effects.TryAddStatusEffectDuration(patient, InfectionSystem.AntibioticEffect, TimeSpan.FromMinutes(1)));
            Assert.That(effects.TryAddStatusEffectDuration(patient, InfectionSystem.AntibioticOverdoseEffect, TimeSpan.FromMinutes(1)));
            Assert.That(infections.OpenWoundInfectionChance(patient, comp), Is.EqualTo(Math.Min(normal * multiplier, 1f)).Within(0.0001f));

            entMan.DeleteEntity(patient);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Every pathogen points at a real narrow-spectrum drug and effect, no two share a drug, and each can be caught
    /// from surgery and from some kind of open wound.
    /// </summary>
    [Test]
    public async Task PathogenPrototypesAreConsistent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ProtoMan;
        var config = proto.Index<SterilityConfigPrototype>(SterilityConfigPrototype.DefaultId);

        Assert.That(config.CureSecondsPerTier, Is.GreaterThanOrEqualTo(config.InfectionTickSeconds));

        var pathogens = proto.EnumeratePrototypes<PathogenPrototype>().ToList();
        Assert.That(pathogens, Is.Not.Empty);
        Assert.That(pathogens.Select(p => p.Drug).Distinct().Count(), Is.EqualTo(pathogens.Count), "one drug per pathogen");
        Assert.That(pathogens.Sum(p => p.SurgeryWeight), Is.GreaterThan(0f), "something must be catchable from surgery");

        Assert.Multiple(() =>
        {
            foreach (var pathogen in pathogens)
            {
                Assert.That(proto.HasIndex(pathogen.Drug), $"{pathogen.ID} drug {pathogen.Drug} doesn't exist");
                Assert.That(proto.HasIndex(pathogen.CureEffect), $"{pathogen.ID} cure effect {pathogen.CureEffect} doesn't exist");
                foreach (var category in pathogen.WoundWeights.Keys)
                    Assert.That(proto.HasIndex(category), $"{pathogen.ID} weights unknown category {category}");
            }

            foreach (var risk in config.OpenWounds.Keys)
                Assert.That(pathogens.Sum(p => (p.WoundWeights.TryGetValue(risk, out var weight) ? weight : 0f)), Is.GreaterThan(0f), $"nothing can infect through {risk}");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The matching narrow-spectrum antibiotic wins an infection back one tier per cure period and finally removes it;
    /// the wrong one does nothing.
    /// </summary>
    [Test]
    public async Task NarrowSpectrumDrugCuresOnlyItsPathogen()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapData = await pair.CreateTestMap();
        var infections = entMan.System<InfectionSystem>();
        var effects = entMan.System<StatusEffectsSystem>();
        var config = server.ProtoMan.Index<SterilityConfigPrototype>(SterilityConfigPrototype.DefaultId);
        var now = server.ResolveDependency<IGameTiming>().CurTime;
        var staph = server.ProtoMan.Index<PathogenPrototype>("Staphylococcus");
        var strep = server.ProtoMan.Index<PathogenPrototype>("Streptococcus");
        var ticks = (int) Math.Ceiling(config.CureSecondsPerTier / config.InfectionTickSeconds);

        await server.WaitAssertion(() =>
        {
            var patient = entMan.SpawnEntity("MobHuman", mapData.GridCoords);
            var comp = entMan.GetComponent<WoundComponent>(patient);

            infections.SetInfection(patient, comp, 3, staph.ID);
            Assert.That(infections.GetPathogen(comp), Is.EqualTo(staph.ID));

            // The wrong drug: it keeps worsening as if untreated
            Assert.That(effects.TryAddStatusEffectDuration(patient, strep.CureEffect, TimeSpan.FromHours(1)));
            Assert.That(infections.IsBeingCured(patient, Infection(comp)), Is.False);
            for (var i = 0; i < ticks * 3; i++)
                infections.Tick(patient, comp, now);
            Assert.That(Infection(comp).Tier, Is.EqualTo(3), "the wrong drug neither cures nor lowers it");

            // The right one: a tier per cure period
            Assert.That(effects.TryAddStatusEffectDuration(patient, staph.CureEffect, TimeSpan.FromHours(1)));
            Assert.That(infections.IsBeingCured(patient, Infection(comp)));
            for (var i = 0; i < ticks - 1; i++)
                infections.Tick(patient, comp, now);
            Assert.That(Infection(comp).Tier, Is.EqualTo(3), "not yet");
            infections.Tick(patient, comp, now);
            Assert.That(Infection(comp).Tier, Is.EqualTo(2));

            for (var i = 0; i < ticks; i++)
                infections.Tick(patient, comp, now);
            Assert.That(Infection(comp).Tier, Is.EqualTo(1));

            for (var i = 0; i < ticks; i++)
                infections.Tick(patient, comp, now);
            Assert.That(Infection(comp), Is.Null, "cured");

            entMan.DeleteEntity(patient);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The repurposed virology machines exist with their new components, and the lathe can make their boards.
    /// </summary>
    [Test]
    public async Task PathogenMachinesAreSetUp()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ProtoMan;

        Assert.That(proto.Index<EntityPrototype>("DiseaseDiagnoser").Components.ContainsKey("PathogenAnalyzer"));
        Assert.That(proto.Index<EntityPrototype>("Vaccinator").Components.ContainsKey("PathogenSynthesizer"));
        Assert.That(proto.HasIndex<LatheRecipePrototype>("DiagnoserMachineCircuitboard"));
        Assert.That(proto.HasIndex<LatheRecipePrototype>("VaccinatorMachineCircuitboard"));

        await pair.CleanReturnAsync();
    }
}
