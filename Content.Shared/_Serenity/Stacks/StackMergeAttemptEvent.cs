namespace Content.Shared._Serenity.Stacks;

/// <summary>
/// Raised on the recipient stack before <paramref name="Donor"/> merges into it. Both are already the same stack type;
/// cancel to keep them apart anyway.
/// </summary>
[ByRefEvent]
public record struct StackMergeAttemptEvent(EntityUid Donor, bool Cancelled = false);
