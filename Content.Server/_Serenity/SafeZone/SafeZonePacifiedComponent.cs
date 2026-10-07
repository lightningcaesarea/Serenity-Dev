namespace Content.Server._Serenity.SafeZone;

/// <summary>
/// Marks a mob whose <c>Pacified</c> came from a safe zone, so leaving the zone only removes pacification the zone
/// added. A mob pacified by anything else (a trait, a smite, the end of the round) never gets this marker.
/// </summary>
[RegisterComponent, Access(typeof(SafeZoneSystem))]
public sealed partial class SafeZonePacifiedComponent : Component
{
}
