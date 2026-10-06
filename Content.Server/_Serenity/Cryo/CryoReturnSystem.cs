// Design inspired by Frontier Station 14's CryoSleep (MIT-era 5be37d18c2 and later): sleep in a pod, your body is
// parked, your mind goes to ghost, and you can wake the same body again. Written fresh on top of Starlight's
// cryostorage, which already parks bodies on a paused map and can put them back (HandleCryostorageReconnection).

using Content.Server.Bed.Cryostorage;
using Content.Server.Chat.Managers;
using Content.Server.Ghost;
using Content.Shared._Serenity.CCVar;
using Content.Shared.Administration;
using Content.Shared.Bed.Cryostorage;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Serenity.Cryo;

/// <summary>
/// Frontier-style cryopods. When a character is stored, its player becomes a ghost that still owns the parked body.
/// Returning to that body (the ghost's return-to-body action, or the <c>uncryo</c> command) wakes it back in its pod,
/// for as long as <see cref="SerenityCCVars.CryoReturnWindow"/> allows. Without a return the character stays asleep
/// and the player can <c>ghostrespawn</c> as someone else.
/// </summary>
public sealed partial class CryoReturnSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private GhostSystem _ghost = default!;
    [Dependency] private CryostorageSystem _cryostorage = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IPlayerManager _players = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CryostorageContainedComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<CryoReturnComponent, PlayerAttachedEvent>(OnPlayerAttached);
    }

    private bool InPausedMap(EntityUid uid)
    {
        return Transform(uid).MapUid is { } map && _map.IsPaused(map);
    }

    private void OnParentChanged(Entity<CryostorageContainedComponent> ent, ref EntParentChangedMessage args)
    {
        if (!_cfg.GetCVar(SerenityCCVars.CryoReturnEnabled) || !InPausedMap(ent))
            return;

        // Every stored body can be woken, whether or not anyone is still in it.
        EnsureComp<CryoReturnComponent>(ent).StoredAt = _timing.CurTime;

        // An online player still in it is left as a ghost that owns the body. Offline players are left alone so
        // reconnecting still takes them straight back (upstream cryostorage's own path).
        if (!_mind.TryGetMind(ent, out var mindId, out var mind) || mind.UserId == null ||
            !_players.TryGetSessionById(mind.UserId.Value, out var session))
        {
            return;
        }

        _ghost.OnGhostAttempt(mindId, true, forced: true);
        _chat.DispatchServerMessage(session, Loc.GetString("serenity-cryo-stored",
            ("minutes", (int) _cfg.GetCVar(SerenityCCVars.CryoReturnWindow))));
    }

    private void OnPlayerAttached(Entity<CryoReturnComponent> ent, ref PlayerAttachedEvent args)
    {
        // A player came back to the parked body (return to body / uncryo): wake it in its pod.
        if (!InPausedMap(ent))
        {
            RemComp<CryoReturnComponent>(ent);
            return;
        }

        var window = TimeSpan.FromMinutes(_cfg.GetCVar(SerenityCCVars.CryoReturnWindow));
        if (_timing.CurTime - ent.Comp.StoredAt > window)
            return;

        if (!TryComp<CryostorageContainedComponent>(ent, out var contained))
            return;

        _cryostorage.HandleCryostorageReconnection((ent, contained));
        RemComp<CryoReturnComponent>(ent);
    }

    /// <summary>
    /// Returns a ghosted, cryo-stored player to their body. Used by the <c>uncryo</c> command.
    /// </summary>
    public bool TryWake(ICommonSession session, out string error)
    {
        error = string.Empty;
        if (!_mind.TryGetMind(session, out var mindId, out var mind) || mind.OwnedEntity is not { } body ||
            !TryComp<CryoReturnComponent>(body, out var cryo))
        {
            error = Loc.GetString("serenity-cryo-nothing-to-wake");
            return false;
        }

        var window = TimeSpan.FromMinutes(_cfg.GetCVar(SerenityCCVars.CryoReturnWindow));
        if (_timing.CurTime - cryo.StoredAt > window)
        {
            error = Loc.GetString("serenity-cryo-window-over");
            return false;
        }

        _mind.UnVisit(mindId, mind);
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
