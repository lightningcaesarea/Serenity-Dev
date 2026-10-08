using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Content.Shared.Stacks;

namespace Content.Shared._Serenity.Power.Tokamak;

/// <summary>
/// Heats the plasma of a <see cref="TokamakCoreComponent"/> that is in front of it.
/// </summary>
[RegisterComponent]
public sealed partial class TokamakGyrotronComponent : Component
{
    [DataField]
    public bool Active;

    /// <summary>
    /// Pulses per second.
    /// </summary>
    [DataField]
    public float Rate = 1f;

    /// <summary>
    /// Energy of each pulse.
    /// </summary>
    [DataField]
    public float MegaEnergy = 1f;

    [DataField]
    public float MaxRate = 5f;

    [DataField]
    public float MaxMegaEnergy = 10f;

    /// <summary>
    /// Kelvin added to the plasma per pulse per unit of mega-energy.
    /// </summary>
    [DataField]
    public float HeatPerMegaEnergy = 400f;

    /// <summary>
    /// How far the beam reaches, in tiles.
    /// </summary>
    [DataField]
    public float Range = 7f;

    /// <summary>
    /// Power drawn while firing, per unit of rate times mega-energy.
    /// </summary>
    [DataField]
    public float DrawPerUnit = 60f;

    [DataField]
    public float IdleDraw = 100f;

    [DataField]
    public SoundSpecifier ActivateSound = new SoundPathSpecifier("/Audio/Weapons/emitter.ogg");
}

/// <summary>
/// Feeds the contents of a <see cref="TokamakFuelRodComponent"/> into the plasma in front of it.
/// </summary>
[RegisterComponent]
public sealed partial class TokamakFuelInjectorComponent : Component
{
    [DataField]
    public bool Active;

    /// <summary>
    /// Units of fuel injected per second.
    /// </summary>
    [DataField]
    public float Rate = 2f;

    [DataField]
    public float MaxRate = 10f;

    [DataField]
    public float Range = 7f;

    [DataField]
    public string RodSlotId = "rod";

    [DataField]
    public float IdleDraw = 50f;

    [DataField]
    public float DrawPerUnit = 25f;

    [DataField]
    public SoundSpecifier ActivateSound = new SoundPathSpecifier("/Audio/Machines/ame_fuelinjection.ogg");
}

/// <summary>
/// A fuel rod holding fusion reactants.
/// </summary>
[RegisterComponent]
public sealed partial class TokamakFuelRodComponent : Component
{
    /// <summary>
    /// Reactant id to amount.
    /// </summary>
    [DataField]
    public Dictionary<string, float> Reactants = new();

    /// <summary>
    /// Amount a full rod holds, used for the fill display.
    /// </summary>
    [DataField]
    public float Capacity = 100f;
}

/// <summary>
/// Pulls transmutation products out of the plasma of a nearby <see cref="TokamakCoreComponent"/>
/// and presses them into material sheets.
/// </summary>
[RegisterComponent]
public sealed partial class TokamakHarvesterComponent : Component
{
    [DataField]
    public bool Active;

    /// <summary>
    /// Reactant id to the stack it is pressed into.
    /// </summary>
    [DataField]
    public Dictionary<string, ProtoId<StackPrototype>> Outputs = new();

    /// <summary>
    /// Units of a reactant needed for one sheet.
    /// </summary>
    [DataField]
    public float UnitsPerSheet = 10f;

    /// <summary>
    /// Units pulled per second per reactant.
    /// </summary>
    [DataField]
    public float PullRate = 2f;

    [DataField]
    public float Range = 8f;

    [DataField]
    public float IdleDraw = 100f;

    /// <summary>
    /// Partially collected reactants waiting to become a sheet.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, float> Buffer = new();

    [DataField]
    public SoundSpecifier ActivateSound = new SoundPathSpecifier("/Audio/Machines/ame_fuelinjection.ogg");
}

/// <summary>
/// A console that monitors and controls the nearest tokamak core.
/// </summary>
[RegisterComponent]
public sealed partial class TokamakConsoleComponent : Component
{
    /// <summary>
    /// How far from the console a core and its devices are found, in tiles.
    /// </summary>
    [DataField]
    public float Range = 40f;
}
