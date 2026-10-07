using Robust.Shared.GameStates;

namespace Content.Shared._Serenity.Social;

/// <summary>
/// Clicking this mob with an empty hand opens a condensed menu of social verbs (hug, strip, intimacy)
/// instead of hugging it straight away.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SocialMenuComponent : Component;
