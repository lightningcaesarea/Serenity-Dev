using Robust.Shared.Prototypes;

namespace Content.Shared._Serenity.Xenobiology;

/// <summary>
/// Raised on a slime processor for each slime it renders down, after that slime's extracts have been spawned.
/// </summary>
/// <param name="Slime">The slime being processed. It is queued for deletion but still exists.</param>
/// <param name="Extract">The extract prototype the slime yields.</param>
/// <param name="Extracts">The extract entities that were spawned for this slime.</param>
[ByRefEvent]
public readonly record struct SlimeProcessedEvent(EntityUid Slime, EntProtoId Extract, List<EntityUid> Extracts);
