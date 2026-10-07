using Content.Shared.Humanoid;
using Robust.Shared.Audio;

namespace Content.Shared._Serenity.Intimacy;

/// <summary>
/// Involuntary vocalisations for an intimacy participant: arousal-driven moans, pain responses,
/// blushing when acted upon, and a climax cry. Sounds are chosen by the mob's sex and, for moans,
/// by how aroused it currently is. Everything here respects the listener's LewdSounds consent
/// because playback goes through <c>IntimacySystem.PlayLewdSound</c>.
/// </summary>
[RegisterComponent]
public sealed partial class IntimacyVocalComponent : Component
{
    /// <summary>Overrides the sex used to pick sounds; otherwise taken from <see cref="HumanoidAppearanceComponent"/>.</summary>
    [DataField]
    public Sex? SexOverride;

    /// <summary>
    /// Moan sounds per sex, checked in order: the first tier whose <see cref="VocalTier.MinArousal"/>
    /// the mob meets is used, so list higher tiers first.
    /// </summary>
    [DataField]
    public Dictionary<Sex, List<VocalTier>> Moans = new()
    {
        [Sex.Male] = new() { new VocalTier(0f, new SoundCollectionSpecifier("IntimacyMoanMale", AudioParams.Default.WithVolume(-4f))) },
        [Sex.Female] = new()
        {
            new VocalTier(60f, new SoundCollectionSpecifier("IntimacyMoanFemaleHigh", AudioParams.Default.WithVolume(-4f))),
            new VocalTier(30f, new SoundCollectionSpecifier("IntimacyMoanFemale", AudioParams.Default.WithVolume(-4f))),
            new VocalTier(0f, new SoundCollectionSpecifier("IntimacyMoanFemaleLight", AudioParams.Default.WithVolume(-5f))),
        },
        [Sex.Unsexed] = new()
        {
            new VocalTier(60f, new SoundCollectionSpecifier("IntimacyMoanFemaleHigh", AudioParams.Default.WithVolume(-4f))),
            new VocalTier(0f, new SoundCollectionSpecifier("IntimacyMoanFemale", AudioParams.Default.WithVolume(-4f))),
        },
    };

    [DataField]
    public Dictionary<Sex, SoundSpecifier> PainMoans = new()
    {
        [Sex.Male] = new SoundCollectionSpecifier("IntimacyPainMale", AudioParams.Default.WithVolume(-4f)),
        [Sex.Female] = new SoundCollectionSpecifier("IntimacyPainFemale", AudioParams.Default.WithVolume(-4f)),
        [Sex.Unsexed] = new SoundCollectionSpecifier("IntimacyPainFemale", AudioParams.Default.WithVolume(-4f)),
    };

    /// <summary>Used when the mob climaxes and its <see cref="ClimaxComponent.Sound"/> is unset.</summary>
    [DataField]
    public Dictionary<Sex, SoundSpecifier> Climax = new()
    {
        [Sex.Male] = new SoundCollectionSpecifier("IntimacyClimaxMale", AudioParams.Default.WithVolume(-3f)),
        [Sex.Female] = new SoundCollectionSpecifier("IntimacyClimaxFemale", AudioParams.Default.WithVolume(-3f)),
        [Sex.Unsexed] = new SoundCollectionSpecifier("IntimacyClimaxFemale", AudioParams.Default.WithVolume(-3f)),
    };

    [DataField]
    public SoundSpecifier? Blush = new SoundCollectionSpecifier("IntimacyBlush", AudioParams.Default.WithVolume(-6f));

    // ---- Triggers ----

    /// <summary>Arousal at or above this lets a pleasurable act on the mob draw a moan.</summary>
    [DataField]
    public float ActMoanArousalThreshold = 10f;

    /// <summary>Chance of moaning when a pleasurable act lands on a mob over <see cref="ActMoanArousalThreshold"/>.</summary>
    [DataField]
    public float ActMoanChance = 0.5f;

    /// <summary>Arousal at or above this makes the mob liable to moan on its own.</summary>
    [DataField]
    public float MoanArousalThreshold = 50f;

    /// <summary>Pleasure at or above this also makes the mob liable to moan on its own.</summary>
    [DataField]
    public float MoanPleasureThreshold = 70f;

    /// <summary>Per-second chance of an automatic moan while over a threshold. 0.05 ≈ one every ~20 s.</summary>
    [DataField]
    public float AutoMoanChance = 0.05f;

    /// <summary>A pain increase that leaves Pain above this may draw a pained sound.</summary>
    [DataField]
    public float PainThreshold = 25f;

    [DataField]
    public float PainMoanChance = 0.6f;

    /// <summary>Chance of blushing when another participant performs an act on this mob.</summary>
    [DataField]
    public float BlushChance = 0.2f;

    /// <summary>Minimum gap between any two automatic vocalisations, so the chat is not flooded.</summary>
    [DataField]
    public TimeSpan MinimumGap = TimeSpan.FromSeconds(4);

    // ---- Emote text ($actor) ----

    [DataField]
    public List<LocId> MoanMessages = new() { "intimacy-vocal-moan-1", "intimacy-vocal-moan-2", "intimacy-vocal-moan-3" };

    [DataField]
    public List<LocId> HighMoanMessages = new() { "intimacy-vocal-moan-high-1", "intimacy-vocal-moan-high-2" };

    [DataField]
    public List<LocId> PainMessages = new() { "intimacy-vocal-pain-1", "intimacy-vocal-pain-2" };

    [DataField]
    public List<LocId> BlushMessages = new() { "intimacy-vocal-blush-1", "intimacy-vocal-blush-2" };

    [ViewVariables]
    public TimeSpan NextTick;

    [ViewVariables]
    public TimeSpan NextVocal;
}

/// <summary>A moan sound used once arousal reaches <see cref="MinArousal"/>.</summary>
[DataDefinition]
public sealed partial class VocalTier
{
    [DataField]
    public float MinArousal;

    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    public VocalTier() { }

    public VocalTier(float minArousal, SoundSpecifier sound)
    {
        MinArousal = minArousal;
        Sound = sound;
    }
}
