using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._Serenity.SafeZone;

/// <summary>
/// Put on a station: players standing on its main grid (the one carrying <c>BecomesStation</c>) are pacified until
/// they leave it. Ships docked to the station are their own grids, so they stay outside the zone.
/// </summary>
[RegisterComponent, Access(typeof(SafeZoneSystem))]
public sealed partial class SafeZoneComponent : Component
{
    /// <summary>
    /// Jobs that keep the ability to fight inside the zone, e.g. security, who have to be able to arrest people.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<JobPrototype>> ExemptJobs = new();
}
