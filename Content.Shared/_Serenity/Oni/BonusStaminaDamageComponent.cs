using Robust.Shared.GameStates;

namespace Content.Shared._Serenity.Oni;

/// <summary>
/// Scales the stamina damage this entity deals with melee weapons that damage stamina on hit.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BonusStaminaDamageComponent : Component
{
    [DataField]
    public float Multiplier = 1f;
}
