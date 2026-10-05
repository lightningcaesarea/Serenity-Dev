using Robust.Shared.GameStates;

namespace Content.Shared._Serenity.Oni;

/// <summary>
/// Widens the spread of every gun this entity holds.
/// Applied by <see cref="Content.Shared.Weapons.Ranged.Systems.SharedGunSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PlayerAccuracyModifierComponent : Component
{
    /// <summary>
    /// Multiplier on the spread cone of held guns. Above 1 is less accurate.
    /// </summary>
    [DataField]
    public float SpreadMultiplier = 1f;

    /// <summary>
    /// The widest spread cone, in degrees, the multiplier is allowed to produce.
    /// </summary>
    [DataField]
    public float MaxSpreadAngle = 180f;
}
