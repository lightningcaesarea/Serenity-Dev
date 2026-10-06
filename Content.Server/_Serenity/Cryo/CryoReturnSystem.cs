// Design inspired by Frontier Station 14's CryoSleep (MIT-era 5be37d18c2 and later): sleep in a pod, your body is
// parked, and you can wake the same body again. Written fresh on top of Starlight's cryostorage, which already parks
// bodies on a paused map and records who they belonged to.

using Content.Server.Chat.Managers;
using Content.Shared._Serenity.CCVar;
using Content.Shared._Starlight.CryoTeleportation;
using Content.Shared.Administration;
using Content.Shared.Bed.Cryostorage;
using Content.Shared.Climbing.Systems;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Serenity.Cryo;

/// <summary>
/// Frontier-style cryopods. Whenever a body is parked in cryostorage (the player went to sleep in the pod and
/// disconnected, or ghosted, or was auto-cryoed) we note whose it is. Their ghost can then wake it with the
/// <c>uncryo</c> command for as long as <see cref="SerenityCCVars.CryoReturnWindow"/> allows: the player's mind moves
/// back into the body, which is put back in its pod. Otherwise the character stays asleep and the player can
/// <c>ghostrespawn</c> as someone else.
/// </summary>
public sealed partial class CryoReturnSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private ClimbSystem _climb = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CryostorageContainedComponent, EntParentChangedMessage>(OnParentChanged);
    }

    private bool InPausedMap(EntityUid uid)
    {
        return Transform(uid).MapUid is { } map && _map.IsPaused(map);
    }

    private void OnParentChanged(Entity<CryostorageContainedComponent> ent, ref EntParentChangedMessage args)
    {
        if (!_cfg.GetCVar(SerenityCCVars.CryoReturnEnabled) || !InPausedMap(ent))
            return;

        // The cryostorage records the owner when their mind leaves or they disconnect; a mind still in the body
        // also tells us.
        var userId = ent.Comp.UserId;
        if (userId == null && TryComp<TargetCryoTeleportationComponent>(ent, out var target))
            userId = target.UserId; // Starlight's auto-cryo remembers who last held the body
        if (userId == null && _mind.TryGetMind(ent, out _, out var mind))
            userId = mind.UserId;

        var cryo = EnsureComp<CryoReturnComponent>(ent);
        cryo.StoredAt = _timing.CurTime;
        cryo.UserId = userId;

        if (userId != null && _players.TryGetSessionById(userId.Value, out var session))
        {
            _chat.DispatchServerMessage(session, Loc.GetString("serenity-cryo-stored",
                ("minutes", (int) _cfg.GetCVar(SerenityCCVars.CryoReturnWindow))));
        }
    }

    /// <summary>
    /// Wakes the stored body that belongs to this player, moving their mind into it.
    /// </summary>
    public bool TryWake(ICommonSession session, out string error)
    {
        error = string.Empty;

        // Only a ghost (or someone with no body) can go back; anyone else would orphan the body they are in.
        if (session.AttachedEntity is { } current && !HasComp<GhostComponent>(current))
        {
            error = Loc.GetString("serenity-cryo-not-ghost");
            return false;
        }

        var window = TimeSpan.FromMinutes(_cfg.GetCVar(SerenityCCVars.CryoReturnWindow));
        EntityUid? best = null;
        var bestStored = TimeSpan.MinValue;
        var expired = false;

        // AllEntityQuery: stored bodies sit on a paused map, which a plain query skips.
        var query = AllEntityQuery<CryoReturnComponent, CryostorageContainedComponent>();
        while (query.MoveNext(out var uid, out var cryo, out _))
        {
            if (cryo.UserId != session.UserId || !InPausedMap(uid))
                continue;

            if (_timing.CurTime - cryo.StoredAt > window)
            {
                expired = true;
                continue;
            }

            if (cryo.StoredAt > bestStored)
            {
                best = uid;
                bestStored = cryo.StoredAt;
            }
        }

        if (best is not { } body)
        {
            error = Loc.GetString(expired ? "serenity-cryo-window-over" : "serenity-cryo-nothing-to-wake");
            return false;
        }

        if (!TryComp<CryostorageContainedComponent>(body, out var contained) ||
            !Wake((body, contained)))
        {
            error = Loc.GetString("serenity-cryo-nothing-to-wake");
            return false;
        }

        // Body is back in its pod: hand the player their character again.
        if (_mind.TryGetMind(session, out var mindId, out var mind))
            _mind.TransferTo(mindId, body, mind: mind);

        RemComp<CryoReturnComponent>(body);
        return true;
    }

    /// <summary>
    /// Puts a stored body back into the pod it was stored from, the same way cryostorage does when a player reconnects.
    /// </summary>
    private bool Wake(Entity<CryostorageContainedComponent> ent)
    {
        var (uid, contained) = ent;

        if (contained.Cryostorage is not { } cryostorage || TerminatingOrDeleted(cryostorage) ||
            !TryComp<CryostorageComponent>(cryostorage, out var pod))
        {
            // The pod is gone: nothing to wake into.
            return false;
        }

        var podXform = Transform(cryostorage);
        _transform.SetParent(uid, podXform.ParentUid);
        _transform.SetCoordinates(uid, podXform.Coordinates);
        if (!_container.TryGetContainer(cryostorage, pod.ContainerId, out var container) ||
            !_container.Insert(uid, container, podXform))
        {
            _climb.ForciblySetClimbing(uid, cryostorage);
        }

        contained.GracePeriodEndTime = null;
        pod.StoredPlayers.Remove(uid);
        return true;
    }
}

[AnyCommand]
public sealed partial class UncryoCommand : LocalizedEntityCommands
{
    [Dependency] private CryoReturnSystem _cryoReturn = default!;

    public override string Command => "uncryo";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-ghostrespawn-no-player"));
            return;
        }

        if (!_cryoReturn.TryWake(player, out var error))
            shell.WriteError(error);
    }
}
