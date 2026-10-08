using Content.Shared._Serenity.Medical.Wounds;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Serenity.Medical.Sterility;

/// <summary>
/// All tuning for surgical sterility: how fast tools and gloves get dirty, how dirty surgery has to be before
/// it harms the patient, and how much harm. Dirtiness runs 0-100 on every tool and pair of gloves.
/// </summary>
[Prototype]
public sealed partial class SterilityConfigPrototype : IPrototype
{
    public const string DefaultId = "DefaultSterility";

    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Dirtiness never exceeds this.
    /// </summary>
    [DataField]
    public float MaxDirtiness = 100f;

    /// <summary>
    /// Dirt a surgery step adds to each surgical tool used, and to the surgeon's gloves, unless the step sets its own.
    /// </summary>
    [DataField]
    public float StepToolDirt = 4f;

    [DataField]
    public float StepGloveDirt = 4f;

    /// <summary>
    /// Counted as dirt when the surgeon wears no gloves / no mask.
    /// </summary>
    [DataField]
    public float MissingGlovesDirt = 25f;

    [DataField]
    public float MissingMaskDirt = 10f;

    /// <summary>
    /// Dirt added per patient, other than the one being operated on, whose DNA is on the tools or gloves.
    /// </summary>
    [DataField]
    public float CrossContaminationDirt = 60f;

    /// <summary>
    /// Total dirtiness at which surgery starts to harm the patient.
    /// </summary>
    [DataField]
    public float SepsisThreshold = 40f;

    /// <summary>
    /// Harm per step once over the threshold: <c>SepsisBaseDamage + excess² / SepsisDamageDivisor</c>, capped at
    /// <see cref="SepsisMaxDamage"/>. Damage is of the type <see cref="SepsisDamageType"/>.
    /// </summary>
    [DataField]
    public float SepsisBaseDamage = 1f;

    [DataField]
    public float SepsisDamageDivisor = 200f;

    [DataField]
    public float SepsisMaxDamage = 12f;

    [DataField]
    public ProtoId<DamageTypePrototype> SepsisDamageType = "Poison";

    // Infection

    /// <summary>
    /// The wound type added for an infection, and its category (whose wounds never heal by themselves).
    /// </summary>
    [DataField]
    public ProtoId<WoundTypePrototype> InfectionWound = "Infection";

    [DataField]
    public ProtoId<WoundCategoryPrototype> InfectionCategory = "Infection";

    /// <summary>
    /// Seconds between infection updates on the server (progression, symptoms, new infections from open wounds).
    /// </summary>
    [DataField]
    public float InfectionTickSeconds = 5f;

    /// <summary>
    /// How many times faster an infection escalates while the patient is overdosed on an antibiotic. The overdose
    /// also cancels the antibiotic's protection, so the infection isn't frozen and new ones aren't blocked.
    /// </summary>
    [DataField]
    public float OverdoseProgressionMultiplier = 3f;

    /// <summary>
    /// How many times likelier a new infection is while the patient is overdosed on an antibiotic. Multiplies the
    /// per-roll chance from dirty surgery and from open wounds; their caps still apply.
    /// </summary>
    [DataField]
    public float OverdoseInfectionChanceMultiplier = 3f;

    /// <summary>
    /// An operation with a total dirtiness above this can infect the patient: the chance is
    /// <c>(dirtiness - threshold) × SurgeryInfectionChancePerDirt</c>, at most <see cref="SurgeryInfectionMaxChance"/>.
    /// Independent of the sepsis damage, so a dirty operation can do both.
    /// </summary>
    [DataField]
    public float SurgeryInfectionThreshold = 30f;

    [DataField]
    public float SurgeryInfectionChancePerDirt = 0.01f;

    [DataField]
    public float SurgeryInfectionMaxChance = 0.9f;

    /// <summary>
    /// Untreated open wounds can become infected. Each update an open wound of one of these categories at
    /// <c>minTier</c> or worse adds <c>chancePerTier × tier</c> to the chance of a new infection.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<WoundCategoryPrototype>, OpenWoundInfectionRisk> OpenWounds = new();

    /// <summary>
    /// Seconds an untreated infection stays at tier 1, 2 and 3 before getting worse. The last entry is unused:
    /// tier 3 is as bad as it gets.
    /// </summary>
    [DataField]
    public float[] EscalationSeconds = [150f, 150f, 0f];

    /// <summary>
    /// Seconds the matching narrow-spectrum antibiotic has to work to bring an infection down one tier. At tier 1
    /// it clears the infection.
    /// </summary>
    [DataField]
    public float CureSecondsPerTier = 45f;

    /// <summary>
    /// Poison damage per infection update at infection tier 1, 2 and 3.
    /// </summary>
    [DataField]
    public float[] SymptomDamage = [0f, 0.25f, 0.6f];

    /// <summary>
    /// Chance that an operation with this total dirtiness infects the patient. An antibiotic overdose multiplies it by
    /// <see cref="OverdoseInfectionChanceMultiplier"/>, still capped at <see cref="SurgeryInfectionMaxChance"/>.
    /// </summary>
    public float SurgeryInfectionChance(float totalDirtiness, bool overdosed = false)
    {
        if (totalDirtiness <= SurgeryInfectionThreshold)
            return 0f;

        var chance = (totalDirtiness - SurgeryInfectionThreshold) * SurgeryInfectionChancePerDirt;
        if (overdosed)
            chance *= OverdoseInfectionChanceMultiplier;

        return Math.Min(chance, SurgeryInfectionMaxChance);
    }

    /// <summary>
    /// Damage done to a patient by an operation with the given total dirtiness; 0 at or below the threshold.
    /// </summary>
    public float SepsisDamage(float totalDirtiness)
    {
        if (totalDirtiness <= SepsisThreshold)
            return 0f;

        var excess = totalDirtiness - SepsisThreshold;
        return Math.Min(SepsisBaseDamage + excess * excess / SepsisDamageDivisor, SepsisMaxDamage);
    }
}

[DataDefinition]
public sealed partial class OpenWoundInfectionRisk
{
    /// <summary>
    /// Wounds below this tier are too minor to get infected.
    /// </summary>
    [DataField]
    public int MinTier = 2;

    /// <summary>
    /// Chance per infection update, per tier of the wound.
    /// </summary>
    [DataField]
    public float ChancePerTier = 0.004f;
}
