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
    /// Jobs that keep the ability to fight inside the zone, on top of <see cref="ExemptDepartments"/>.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<JobPrototype>> ExemptJobs = new();

    /// <summary>
    /// Departments whose jobs keep the ability to fight inside the zone. A job counts if it is in any of these.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<DepartmentPrototype>> ExemptDepartments = new();
}
