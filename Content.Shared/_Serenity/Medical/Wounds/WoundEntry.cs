using Content.Shared._Serenity.Medical.Sterility;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Serenity.Medical.Wounds;

[DataDefinition, Serializable, NetSerializable]
public sealed partial class WoundEntry
{
    [DataField]
    public ProtoId<WoundTypePrototype> WoundTypeId;

    [DataField]
    public int Tier;

    /// <summary>
    /// Game time at which this wound's next natural-regen tier drop is due.
    /// Set by <see cref="Systems.SharedWoundSystem"/> on application or tier
    /// upgrade and decremented by the server-side regen tick. Replicated so
    /// the client never lags the actual tier value.
    /// </summary>
    [DataField]
    public TimeSpan NextDecayTime;

    /// <summary>
    /// Where on the body the wound is, or null for a wound that affects the whole body.
    /// </summary>
    [DataField]
    public WoundLocation? Location;

    /// <summary>
    /// For an infection, the strain behind it, which decides what cures it. Null for every other wound, and for an
    /// infection with no particular strain (nothing but surgery cures it).
    /// </summary>
    [DataField]
    public ProtoId<PathogenPrototype>? Pathogen;

    /// <summary>
    /// For an infection, seconds a matching narrow-spectrum antibiotic has worked on its current tier. Only the
    /// server uses this.
    /// </summary>
    [DataField]
    public float CureProgress;

    public WoundEntry(ProtoId<WoundTypePrototype> woundTypeId, int tier)
    {
        WoundTypeId = woundTypeId;
        Tier = tier;
    }
}
