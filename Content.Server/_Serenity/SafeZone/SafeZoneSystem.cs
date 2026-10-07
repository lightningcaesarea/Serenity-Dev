using Content.Server.Popups;
using Content.Server.Station.Components;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Station.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Serenity.SafeZone;

/// <summary>
/// Pacifies players while they stand on the main grid of a station with a <see cref="SafeZoneComponent"/>, and lifts
/// it again when they leave. Checked once a second rather than on every move.
/// </summary>
public sealed class SafeZoneSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private SharedMindSystem _mind = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + UpdateInterval;

        // Lift the zone's pacification from anyone who has left (or is now exempt), connected or not.
        var pacified = EntityQueryEnumerator<SafeZonePacifiedComponent, TransformComponent>();
        while (pacified.MoveNext(out var uid, out _, out var xform))
        {
            if (InSafeZone(uid, xform))
                continue;

            RemComp<SafeZonePacifiedComponent>(uid);
            RemComp<PacifiedComponent>(uid);
            _popup.PopupEntity(Loc.GetString("safe-zone-leave"), uid, uid);
        }

        // Pacify players who have walked in. Mobs only, so ghosts passing through are left alone.
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (HasComp<PacifiedComponent>(uid) || !InSafeZone(uid, xform))
                continue;

            AddComp<PacifiedComponent>(uid);
            AddComp<SafeZonePacifiedComponent>(uid);
            _popup.PopupEntity(Loc.GetString("safe-zone-enter"), uid, uid);
        }
    }

    /// <summary>
    /// Whether this mob is on a safe zone's main grid and its job is not exempt.
    /// </summary>
    public bool InSafeZone(EntityUid uid, TransformComponent? xform = null)
    {
        if (!Resolve(uid, ref xform)
            || xform.GridUid is not { } grid
            || !HasComp<BecomesStationComponent>(grid)
            || !TryComp<StationMemberComponent>(grid, out var member)
            || !TryComp<SafeZoneComponent>(member.Station, out var zone))
            return false;

        if (zone.ExemptJobs.Count == 0 && zone.ExemptDepartments.Count == 0
            || !_mind.TryGetMind(uid, out var mindId, out _)
            || !_jobs.MindTryGetJobId(mindId, out var job)
            || job is not { } jobId)
            return true;

        return !IsExempt(zone, jobId);
    }

    private bool IsExempt(SafeZoneComponent zone, ProtoId<JobPrototype> job)
    {
        if (zone.ExemptJobs.Contains(job))
            return true;

        if (zone.ExemptDepartments.Count == 0 || !_jobs.TryGetAllDepartments(job, out var departments))
            return false;

        foreach (var department in departments)
        {
            if (zone.ExemptDepartments.Contains(department.ID))
                return true;
        }

        return false;
    }
}
