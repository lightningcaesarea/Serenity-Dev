namespace Content.Shared._Serenity.Botany;

/// <summary>
/// Raised on a tray just before the plant in it is dug up with a shovel.
/// </summary>
[ByRefEvent]
public readonly record struct PlantDugUpEvent(EntityUid Plant);
