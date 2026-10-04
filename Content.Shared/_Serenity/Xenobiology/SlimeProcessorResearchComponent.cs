using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._Serenity.Xenobiology;

/// <summary>
/// Lets a slime processor pay research points to its research server for every extract it produces.
/// An extract's value comes from its tier tag, so rarer colours pay more. The first extract of a colour on a
/// server pays a discovery bonus, and each further extract of that colour pays less than the one before it,
/// so farming one colour runs dry and pushes science on to new mutations.
/// </summary>
[RegisterComponent]
public sealed partial class SlimeProcessorResearchComponent : Component
{
    /// <summary>
    /// Research points for the first extract of a colour, keyed by the extract's tier tag.
    /// An extract with none of these tags pays nothing.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<TagPrototype>, int> TierPoints = new();

    /// <summary>
    /// The first extract of a colour pays this many times its tier value on top of the normal payout.
    /// </summary>
    [DataField]
    public float DiscoveryBonus = 3f;

    /// <summary>
    /// Each further extract of the same colour pays this fraction of the one before it.
    /// With 0.8 a colour can pay at most five times its tier value plus the discovery bonus over a round.
    /// </summary>
    [DataField]
    public float Decay = 0.8f;
}
