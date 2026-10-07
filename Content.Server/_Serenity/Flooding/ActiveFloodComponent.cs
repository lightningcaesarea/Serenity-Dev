using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Serenity.Flooding;

/// <summary>
///     Marks a flood that may still have somewhere to flow. Removed once its level matches every open neighbour.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class ActiveFloodComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextFlow;
}
