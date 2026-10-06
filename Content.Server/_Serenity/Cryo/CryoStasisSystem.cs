using Content.Shared._Serenity.CCVar;
using Content.Shared.Bed.Cryostorage;
using Content.Shared.Mind.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._Serenity.Cryo;

/// <summary>
/// Serenity's cryopod rules. Someone who goes to sleep in a pod keeps their body, gear and job slot for an hour
/// (the pod's grace periods, see <c>CryogenicSleepUnit</c>); if they reconnect or climb out in that time nothing is lost.
/// When the hour runs out the character is placed in the stasis dimension (cryostorage's paused map) and the job slot
/// opens up. This system adds the other way in: leaving the body asleep in the pod for good, by ghosting or by
/// switching to another character, sends it to stasis at once instead of waiting out the hour. A body that has sat in
/// stasis for <see cref="SerenityCCVars.CryoStasisLifetime"/> (two hours) is removed, gear and all.
/// </summary>
/// <remarks>
/// Polled rather than event-driven: cryostorage already owns the mind-removed subscription on its component and the
/// engine allows only one, and polling also sidesteps any ordering race with its handler. Cryostorage records the owner
/// in <see cref="CryostorageContainedComponent.UserId"/> when their mind leaves or they disconnect; an owner on record
/// with no mind in the body means the player left it (a disconnected player's mind is still attached).
/// </remarks>
public sealed partial class CryoStasisSystem : EntitySystem
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _map = default!;

    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CryostorageContainedComponent, EntParentChangedMessage>(OnParentChanged);
    }

    private bool InStasis(EntityUid uid)
    {
        return Transform(uid).MapUid is { } map && _map.IsPaused(map);
    }

    private void OnParentChanged(Entity<CryostorageContainedComponent> ent, ref EntParentChangedMessage args)
    {
        if (InStasis(ent))
            EnsureComp<CryoStasisComponent>(ent).EnteredAt = _timing.CurTime;
        else
            RemComp<CryoStasisComponent>(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;

        // Bodies still asleep in a pod: a player who left them goes to stasis now, not after the hour.
        var query = EntityQueryEnumerator<CryostorageContainedComponent, MindContainerComponent>();
        while (query.MoveNext(out var uid, out var contained, out var mind))
        {
            // Only bodies still asleep in a pod have a grace period running; bodies already in stasis have none.
            if (contained.GracePeriodEndTime is not { } end || contained.UserId == null || mind.HasMind)
                continue;

            if (end <= _timing.CurTime)
                continue;

            contained.GracePeriodEndTime = _timing.CurTime;
            Dirty(uid, contained);
        }

        // Bodies in stasis: remove them once their time is up. AllEntityQuery, because the stasis map is paused.
        var lifetime = TimeSpan.FromMinutes(_cfg.GetCVar(SerenityCCVars.CryoStasisLifetime));
        var stasis = AllEntityQuery<CryoStasisComponent>();
        while (stasis.MoveNext(out var uid, out var comp))
        {
            if (_timing.CurTime - comp.EnteredAt >= lifetime)
                QueueDel(uid);
        }
    }
}
