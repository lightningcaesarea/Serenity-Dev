using Robust.Shared.GameStates;

namespace Content.Shared._Serenity.Oni;

/// <summary>
/// Reduces how much worn clothing slows this entity down.
/// Applied by <see cref="Content.Shared.Clothing.ClothingSpeedModifierSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ClothingSlowResistanceComponent : Component
{
    /// <summary>
    /// Fraction of each clothing slowdown that is ignored: 0.25 makes every slowdown 25% weaker.
    /// </summary>
    [DataField]
    public float Modifier;
}
