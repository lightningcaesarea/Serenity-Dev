namespace Content.Server._Serenity.Cryo;

/// <summary>
/// On a body in the cryo stasis dimension, noting when it arrived so <see cref="CryoStasisSystem"/> can remove it.
/// </summary>
[RegisterComponent]
public sealed partial class CryoStasisComponent : Component
{
    /// <summary>
    /// Server time the body entered stasis.
    /// </summary>
    [DataField]
    public TimeSpan EnteredAt;
}
