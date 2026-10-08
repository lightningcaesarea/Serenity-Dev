namespace Content.Shared._Serenity.ArmorPlate;

/// <summary>
/// Added to a mob while it wears plate-carrying clothing with a plate in it, so only those mobs
/// run the plate logic when they take damage.
/// </summary>
[RegisterComponent]
public sealed partial class ArmorPlateWearerComponent : Component;
