using Robust.Shared.Network;

namespace Content.Server._Serenity.Cryo;

/// <summary>
/// On a body parked in cryostorage, remembering whose it is so they can wake it again. See <see cref="CryoReturnSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class CryoReturnComponent : Component
{
    /// <summary>
    /// Server time the body went into the pod.
    /// </summary>
    [DataField]
    public TimeSpan StoredAt;

    /// <summary>
    /// The player who owned the body when it was stored, if known.
    /// </summary>
    [DataField]
    public NetUserId? UserId;
}
