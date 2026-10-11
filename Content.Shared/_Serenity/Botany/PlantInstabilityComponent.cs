using Robust.Shared.GameStates;

namespace Content.Shared._Serenity.Botany;

/// <summary>
/// How unstable a plant's genes are. Every growth cycle an unstable plant may mutate on its own: the higher the
/// instability, the stronger and more frequent the mutations, up to changing species outright.
/// Carried on seeds and produce (it is part of the plant clone settings) and averaged on cross-pollination.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
[Access(typeof(PlantInstabilitySystem))]
public sealed partial class PlantInstabilityComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Instability = 5f;

    [DataField]
    public float MaxInstability = 100f;

    /// <summary>
    /// From this instability the plant can drift in its stats a little each growth cycle.
    /// </summary>
    [DataField]
    public float SoftThreshold = 20f;

    /// <summary>
    /// From this instability the drift is twice as strong.
    /// </summary>
    [DataField]
    public float HardThreshold = 40f;

    /// <summary>
    /// From this instability the plant can turn into one of its mutation species.
    /// </summary>
    [DataField]
    public float SpeciesThreshold = 60f;

    /// <summary>
    /// From this instability the plant can gain a new produce chemical.
    /// </summary>
    [DataField]
    public float TraitThreshold = 80f;

    /// <summary>
    /// Chance of a stat drift per growth cycle is the instability times this.
    /// </summary>
    [DataField]
    public float StatChancePerPoint = 0.004f;

    /// <summary>
    /// Chance of a species change per growth cycle is the instability times this.
    /// </summary>
    [DataField]
    public float SpeciesChancePerPoint = 0.0025f;

    /// <summary>
    /// Chance of a new produce chemical per growth cycle is the instability above the trait baseline times this.
    /// </summary>
    [DataField]
    public float TraitChancePerPoint = 0.01f;

    /// <summary>
    /// The instability a plant needs before it starts gaining chemicals, for the chance above.
    /// </summary>
    [DataField]
    public float TraitBaseline = 70f;
}
