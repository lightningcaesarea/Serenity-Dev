using Robust.Shared.Prototypes;

namespace Content.Shared._Serenity.Power.Tokamak;

/// <summary>
/// A fusion reaction that can happen between two reactants inside a tokamak plasma.
/// </summary>
[Prototype]
public sealed partial class FusionReactionPrototype : IPrototype
{
    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// First reactant. May be the same as <see cref="ReactantB"/>.
    /// </summary>
    [DataField(required: true)]
    public string ReactantA = default!;

    [DataField(required: true)]
    public string ReactantB = default!;

    /// <summary>
    /// Minimum plasma temperature, in kelvin, for the reaction to happen.
    /// </summary>
    [DataField]
    public float MinTemperature;

    /// <summary>
    /// Amount of each reactant consumed per second at most.
    /// </summary>
    [DataField]
    public float Rate = 2f;

    /// <summary>
    /// Kelvin added to (or removed from, if negative) the plasma per unit consumed.
    /// </summary>
    [DataField]
    public float HeatPerUnit;

    /// <summary>
    /// Radiation produced per unit consumed.
    /// </summary>
    [DataField]
    public float Radiation;

    /// <summary>
    /// Instability produced per unit consumed.
    /// </summary>
    [DataField]
    public float Instability;

    /// <summary>
    /// Reactants produced per unit consumed.
    /// </summary>
    [DataField]
    public Dictionary<string, float> Products = new();

    /// <summary>
    /// Lower priorities react first.
    /// </summary>
    [DataField]
    public int Priority;
}
