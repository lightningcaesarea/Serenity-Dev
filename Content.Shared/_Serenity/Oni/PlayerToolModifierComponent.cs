using Robust.Shared.GameStates;

namespace Content.Shared._Serenity.Oni;

/// <summary>
/// Scales how long this entity takes to pry things open with its bare hands or a tool.
/// Applied by <see cref="Content.Shared.Prying.Systems.PryingSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PlayerToolModifierComponent : Component
{
    /// <summary>
    /// Multiplier on the pry do-after time. Below 1 is faster.
    /// </summary>
    [DataField]
    public float PryTimeMultiplier = 1f;
}
