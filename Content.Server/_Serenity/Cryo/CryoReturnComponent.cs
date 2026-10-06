namespace Content.Server._Serenity.Cryo;

/// <summary>
/// On a body parked in cryostorage whose owner was ghosted, so it can be woken again. See <see cref="CryoReturnSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class CryoReturnComponent : Component
{
    /// <summary>
    /// Server time the body went into the pod.
    /// </summary>
    [DataField]
    public TimeSpan StoredAt;
}
