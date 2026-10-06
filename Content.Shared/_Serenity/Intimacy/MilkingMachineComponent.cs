using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Serenity.Intimacy;

/// <summary>
/// A chair that pumps body fluids out of whoever is buckled into it and into its tank, which plumbing
/// can pull from. What it collects is data-driven: each <see cref="MilkingSource"/> needs an intimacy
/// feature tag on the occupant (for example <c>BreastsAccessible</c>).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MilkingMachineComponent : Component
{
    [DataField, AutoNetworkedField]
    public MilkingMachineMode Mode = MilkingMachineMode.Off;

    /// <summary>
    /// Solution on the machine that collected fluid goes into. Plumbing outlets pull from it.
    /// </summary>
    [DataField]
    public string SolutionName = "tank";

    /// <summary>
    /// Multiplier on every rate below for each mode. Off is always zero.
    /// </summary>
    [DataField]
    public Dictionary<MilkingMachineMode, float> ModeStrength = new()
    {
        [MilkingMachineMode.Low] = 1f,
        [MilkingMachineMode.Medium] = 2f,
        [MilkingMachineMode.High] = 3f,
    };

    /// <summary>
    /// Intimacy stats raised on the occupant per second at strength 1.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<IntimacyStatPrototype>, float> StatsPerSecond = new()
    {
        ["Arousal"] = 1.5f,
        ["Pleasure"] = 1f,
    };

    [DataField]
    public List<MilkingSource> Sources = new();

    /// <summary>
    /// Consent toggle the occupant needs before anyone else may switch the machine on them.
    /// </summary>
    [DataField]
    public string OthersToggle = "LewdContainerFillByOthers";

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    [ViewVariables]
    public TimeSpan NextUpdate;
}

[DataDefinition]
public sealed partial class MilkingSource
{
    /// <summary>
    /// Intimacy feature tag the occupant must have, e.g. <c>BreastsAccessible</c>.
    /// </summary>
    [DataField(required: true)]
    public string Feature = string.Empty;

    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    /// <summary>
    /// Collected every second while running, at strength 1.
    /// </summary>
    [DataField]
    public FixedPoint2 PerSecond;

    /// <summary>
    /// Collected once when the occupant climaxes while the machine is running.
    /// </summary>
    [DataField]
    public FixedPoint2 OnClimax;
}

[Serializable, NetSerializable]
public enum MilkingMachineMode : byte
{
    Off,
    Low,
    Medium,
    High,
}

[Serializable, NetSerializable]
public enum MilkingMachineVisuals : byte
{
    Mode,
    Fill,
}

[Serializable, NetSerializable]
public enum MilkingMachineFill : byte
{
    Empty,
    Low,
    Medium,
    High,
    Full,
}

[Serializable, NetSerializable]
public enum MilkingMachineLayers : byte
{
    Base,
    Liquid,
    Indicator,
    Locks,
}
