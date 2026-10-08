using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Serenity.ArmorPlate;

/// <summary>
/// An armor plate. Slotted into the storage of clothing with <see cref="ArmorPlateCarrierComponent"/>,
/// it takes hits for the wearer before their armor is applied, and wears down as it does.
/// The plate's durability is its own damage: give it Damageable and a Destructible threshold
/// equal to <see cref="Durability"/>.
/// </summary>
/// <remarks>
/// Serenity reimplementation of the armor plate design from Monolith (Monolith-Station/Monolith).
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArmorPlateComponent : Component
{
    /// <summary>
    /// Damage the plate can take before it breaks. Null for a plate that never breaks.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int? Durability;

    /// <summary>
    /// Fraction of each damage type the plate stops, from 0 to 1.
    /// A negative value makes the wearer take that much more of the type instead.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<DamageTypePrototype>, float> Absorption = new();

    /// <summary>
    /// How much of each incoming damage type is taken off the plate's durability, as a multiplier of the hit.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<DamageTypePrototype>, float> Wear = new();

    [DataField, AutoNetworkedField]
    public float WalkSpeedModifier = 1f;

    [DataField, AutoNetworkedField]
    public float SprintSpeedModifier = 1f;

    /// <summary>
    /// Stamina damage dealt to the wearer per point of damage the plate stopped.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float StaminaPerAbsorbed;

    /// <summary>
    /// Stamina damage dealt to the wearer per point of extra damage a negative absorption added.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float StaminaPerAmplified;

    /// <summary>
    /// Stamina damage dealt to the wearer per point of incoming damage, whatever the plate did with it.
    /// When set, the two values above are ignored.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float StaminaPerHit;
}
