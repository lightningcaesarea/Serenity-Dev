namespace Content.Shared._Serenity.Docks;

/// <summary>
/// A pad that moves whoever activates it to its partner pad. Each spawned dock has one in the middle of
/// its hall, linked to a matching pad placed beside the console that requested the dock.
/// </summary>
[RegisterComponent]
public sealed partial class DockTeleporterComponent : Component
{
    /// <summary>The pad this one sends people to. Set when the dock is spawned.</summary>
    [ViewVariables]
    public EntityUid? Partner;
}
