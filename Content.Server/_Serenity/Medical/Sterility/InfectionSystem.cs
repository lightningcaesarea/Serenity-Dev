using System.Linq;
using Content.Shared._Serenity.Medical.Sterility;
using Content.Shared._Serenity.Medical.Wounds;
using Content.Shared._Serenity.Medical.Wounds.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusEffectNew;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Serenity.Medical.Sterility;

/// <summary>
/// Infection: a wound that never heals by itself. An unsterile operation or an untreated open wound can give a
/// patient one, caused by one of the strains in <see cref="PathogenPrototype"/>; left alone it gets worse tier by tier
/// and poisons them. A broad-spectrum antibiotic freezes it where it is, suppresses its symptoms and blocks new
/// infections, but doesn't cure it. Surgery (draining it) does, and so does the narrow-spectrum antibiotic that
/// matches its strain, which brings it down a tier at a time until it is gone.
/// All tuning is in <see cref="SterilityConfigPrototype"/>.
/// </summary>
public sealed partial class InfectionSystem : EntitySystem
{
    /// <summary>
    /// Status effect an antibiotic applies.
    /// </summary>
    public static readonly EntProtoId AntibioticEffect = "StatusEffectAntibiotic";

    /// <summary>
    /// Status effect an antibiotic overdose applies. It cancels the antibiotic's protection and speeds up infections.
    /// </summary>
    public static readonly EntProtoId AntibioticOverdoseEffect = "StatusEffectAntibioticOverdose";

    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedWoundSystem _wounds = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundComponent, SurgeryStepDirtiedEvent>(OnStepDirtied);
        _nextTick = _timing.CurTime + TimeSpan.FromSeconds(Config.InfectionTickSeconds);
    }

    private SterilityConfigPrototype Config => _proto.Index<SterilityConfigPrototype>(SterilityConfigPrototype.DefaultId);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextTick)
            return;

        var now = _timing.CurTime;
        _nextTick = now + TimeSpan.FromSeconds(Config.InfectionTickSeconds);

        var query = EntityQueryEnumerator<WoundComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.ActiveWounds.Count == 0 || _mobState.IsDead(uid))
                continue;

            Tick(uid, comp, now);
        }
    }

    private void OnStepDirtied(Entity<WoundComponent> ent, ref SurgeryStepDirtiedEvent args)
    {
        if (HasAntibiotic(ent))
            return;

        if (_random.Prob(Config.SurgeryInfectionChance(args.TotalDirtiness, IsOverdosed(ent))))
            TryInfect(ent, ent.Comp);
    }

    /// <summary>
    /// Whether the mob is protected by an antibiotic. An overdose wipes that protection out.
    /// </summary>
    public bool HasAntibiotic(EntityUid uid)
    {
        return _statusEffects.HasStatusEffect(uid, AntibioticEffect) && !IsOverdosed(uid);
    }

    public bool IsOverdosed(EntityUid uid)
    {
        return _statusEffects.HasStatusEffect(uid, AntibioticOverdoseEffect);
    }

    /// <summary>
    /// Whether the mob currently has an infection.
    /// </summary>
    public bool IsInfected(WoundComponent comp)
    {
        return GetInfection(comp) != null;
    }

    /// <summary>
    /// The strain behind the mob's infection, or null if it has no infection or its infection has no particular strain.
    /// </summary>
    public ProtoId<PathogenPrototype>? GetPathogen(WoundComponent comp)
    {
        return GetInfection(comp)?.Pathogen;
    }

    /// <summary>
    /// Whether the narrow-spectrum antibiotic for the infection's strain is working on it right now.
    /// </summary>
    public bool IsBeingCured(EntityUid uid, WoundEntry infection)
    {
        return infection.Pathogen is { } id
            && _proto.TryIndex(id, out var pathogen)
            && _statusEffects.HasStatusEffect(uid, pathogen.CureEffect);
    }

    /// <summary>
    /// Gives the mob a tier 1 infection, unless it already has one. Returns true if it was added. The strain is the
    /// given one, or else is picked at random for the <paramref name="source"/>: the category of the open wound it
    /// started in, or null for a dirty operation.
    /// </summary>
    public bool TryInfect(
        EntityUid uid,
        WoundComponent comp,
        ProtoId<PathogenPrototype>? pathogen = null,
        ProtoId<WoundCategoryPrototype>? source = null)
    {
        if (GetInfection(comp) != null)
            return false;

        var config = Config;
        _wounds.AddWound(uid, comp, new WoundEntry(config.InfectionWound, 1)
        {
            NextDecayTime = _timing.CurTime + EscalationDelay(config, 1),
            Pathogen = pathogen ?? PickPathogen(source),
        });
        return true;
    }

    /// <summary>
    /// Picks a strain at random, weighted by how likely each is to start in the given kind of open wound, or in a dirty
    /// operation if <paramref name="source"/> is null. Null if there are no strains.
    /// </summary>
    public ProtoId<PathogenPrototype>? PickPathogen(ProtoId<WoundCategoryPrototype>? source = null)
    {
        var weights = new List<(string Id, float Weight)>();
        foreach (var pathogen in _proto.EnumeratePrototypes<PathogenPrototype>())
        {
            var weight = source is { } category
                ? pathogen.WoundWeights.GetValueOrDefault(category)
                : pathogen.SurgeryWeight;

            if (weight > 0f)
                weights.Add((pathogen.ID, weight));
        }

        // No strain starts in this kind of wound: it is as likely as an operation to be any of them
        if (weights.Count == 0)
            return source != null ? PickPathogen() : null;

        var roll = _random.NextFloat() * weights.Sum(w => w.Weight);
        foreach (var (id, weight) in weights)
        {
            roll -= weight;
            if (roll < 0f)
                return new ProtoId<PathogenPrototype>(id);
        }

        return new ProtoId<PathogenPrototype>(weights[^1].Id);
    }

    /// <summary>
    /// Gives the mob an infection at the given tier, or moves its existing infection to that tier. For admin and test
    /// tooling: it ignores antibiotics and sterility. The strain is the given one (changing the existing infection's
    /// if need be), or a random one for a new infection. Returns the tier it was set to.
    /// </summary>
    public int SetInfection(EntityUid uid, WoundComponent comp, int tier, ProtoId<PathogenPrototype>? pathogen = null)
    {
        tier = Math.Clamp(tier, 1, WoundsConstants.MaxWoundTier);
        TryInfect(uid, comp, pathogen);

        if (GetInfection(comp) is not { } infection)
            return 0;

        if (pathogen != null)
            infection.Pathogen = pathogen;

        infection.CureProgress = 0f;
        infection.Tier = tier;
        infection.NextDecayTime = tier >= WoundsConstants.MaxWoundTier
            ? TimeSpan.MaxValue
            : _timing.CurTime + EscalationDelay(Config, tier);
        Dirty(uid, comp);
        RaiseLocalEvent(uid, new WoundsDamagedEvent());
        return tier;
    }

    /// <summary>
    /// The chance per update that the mob's untreated open wounds give it a new infection. 0 if it is already
    /// infected or on an antibiotic. An antibiotic overdose makes it much likelier.
    /// </summary>
    public float OpenWoundInfectionChance(EntityUid uid, WoundComponent comp)
    {
        if (GetInfection(comp) != null || HasAntibiotic(uid))
            return 0f;

        var chance = OpenWoundRisks(comp).Sum(r => r.Chance);

        if (IsOverdosed(uid))
            chance *= Config.OverdoseInfectionChanceMultiplier;

        return Math.Min(chance, 1f);
    }

    /// <summary>
    /// What each open wound that can get infected adds to the chance of a new infection, and its category.
    /// </summary>
    private IEnumerable<(ProtoId<WoundCategoryPrototype> Category, float Chance)> OpenWoundRisks(WoundComponent comp)
    {
        var risks = Config.OpenWounds;
        foreach (var wound in comp.ActiveWounds)
        {
            if (!_proto.TryIndex(wound.WoundTypeId, out var type)
                || !risks.TryGetValue(type.Category, out var risk)
                || wound.Tier < risk.MinTier)
            {
                continue;
            }

            yield return (type.Category, risk.ChancePerTier * wound.Tier);
        }
    }

    /// <summary>
    /// The category of the open wound a new infection starts in, picked in proportion to the risk each wound carries.
    /// </summary>
    private ProtoId<WoundCategoryPrototype>? PickWoundSource(WoundComponent comp)
    {
        var risks = OpenWoundRisks(comp).ToList();
        var total = risks.Sum(r => r.Chance);
        if (total <= 0f)
            return null;

        var roll = _random.NextFloat() * total;
        foreach (var (category, chance) in risks)
        {
            roll -= chance;
            if (roll < 0f)
                return category;
        }

        return risks[^1].Category;
    }

    /// <summary>
    /// One infection update: moves an existing infection along, deals its symptoms, and rolls for a new infection
    /// from open wounds. Public so tests can drive it with their own clock.
    /// </summary>
    public void Tick(EntityUid uid, WoundComponent comp, TimeSpan now)
    {
        var config = Config;
        var antibiotic = HasAntibiotic(uid);

        if (GetInfection(comp) is { } infection)
        {
            // The narrow-spectrum antibiotic for its strain wins it back a tier at a time. While it works the infection
            // can't get worse and its symptoms are suppressed, as with a broad-spectrum one.
            if (IsBeingCured(uid, infection))
            {
                Cure(uid, comp, infection, now, config);
                return;
            }

            // A broad-spectrum antibiotic freezes the infection at its current tier: its timer restarts so it can't
            // worsen, and its symptoms are suppressed. It is not cured, so it resumes when the drug wears off.
            if (antibiotic)
            {
                Freeze(infection, now, config);
                return;
            }

            // An overdose shortens the current tier's timer each update, so the infection escalates faster.
            if (IsOverdosed(uid) && infection.Tier < WoundsConstants.MaxWoundTier)
                infection.NextDecayTime -= TimeSpan.FromSeconds(config.InfectionTickSeconds * (config.OverdoseProgressionMultiplier - 1f));

            Progress(uid, comp, infection, now, config);
            Symptoms(uid, infection, config);
            return;
        }

        if (_random.Prob(OpenWoundInfectionChance(uid, comp)))
            TryInfect(uid, comp, source: PickWoundSource(comp));
    }

    private void Cure(EntityUid uid, WoundComponent comp, WoundEntry infection, TimeSpan now, SterilityConfigPrototype config)
    {
        infection.CureProgress += config.InfectionTickSeconds;
        if (infection.CureProgress < config.CureSecondsPerTier)
        {
            Freeze(infection, now, config);
            return;
        }

        infection.CureProgress = 0f;

        // The last tier is the end of it
        if (infection.Tier <= 1)
        {
            _wounds.RemoveWound(uid, comp, infection);
            return;
        }

        infection.Tier -= 1;
        infection.NextDecayTime = now + EscalationDelay(config, infection.Tier);
        Dirty(uid, comp);
        RaiseLocalEvent(uid, new WoundsDamagedEvent());
    }

    private static void Freeze(WoundEntry infection, TimeSpan now, SterilityConfigPrototype config)
    {
        // Tier 3 never escalates, so it has no timer to hold. Only the server reads this timer, so it isn't networked.
        if (infection.Tier < WoundsConstants.MaxWoundTier)
            infection.NextDecayTime = now + EscalationDelay(config, infection.Tier);
    }

    private void Progress(EntityUid uid, WoundComponent comp, WoundEntry infection, TimeSpan now, SterilityConfigPrototype config)
    {
        if (infection.NextDecayTime > now || infection.Tier >= WoundsConstants.MaxWoundTier)
            return;

        infection.Tier += 1;
        infection.NextDecayTime = infection.Tier >= WoundsConstants.MaxWoundTier
            ? TimeSpan.MaxValue
            : now + EscalationDelay(config, infection.Tier);
        Dirty(uid, comp);
        RaiseLocalEvent(uid, new WoundsDamagedEvent());
    }

    private void Symptoms(EntityUid uid, WoundEntry infection, SterilityConfigPrototype config)
    {
        var index = infection.Tier - WoundsConstants.TierIndexToTierOffset;
        if (index < 0 || index >= config.SymptomDamage.Length || config.SymptomDamage[index] <= 0f)
            return;

        var damage = new DamageSpecifier(_proto.Index(config.SepsisDamageType), FixedPoint2.New(config.SymptomDamage[index]));
        _damageable.TryChangeDamage(uid, damage, ignoreResistances: true, interruptsDoAfters: false);
    }

    private WoundEntry? GetInfection(WoundComponent comp)
    {
        foreach (var wound in comp.ActiveWounds)
        {
            if (wound.WoundTypeId == Config.InfectionWound)
                return wound;
        }

        return null;
    }

    private static TimeSpan EscalationDelay(SterilityConfigPrototype config, int tier)
    {
        var index = tier - WoundsConstants.TierIndexToTierOffset;
        return TimeSpan.FromSeconds(index >= 0 && index < config.EscalationSeconds.Length ? config.EscalationSeconds[index] : 0f);
    }
}
