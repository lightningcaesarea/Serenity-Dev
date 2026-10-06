// Design inspired by Frontier Station 14's ghostrespawn command; written fresh for Serenity.

using Content.Server.GameTicking;
using Content.Shared._Serenity.CCVar;
using Content.Shared.Administration;
using Content.Shared.Ghost;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._Serenity.Respawn;

/// <summary>
/// Lets a ghost return to the lobby, to rejoin as another character, once it has been a ghost for
/// <see cref="SerenityCCVars.RespawnTime"/> seconds.
/// </summary>
[AnyCommand]
public sealed partial class GhostRespawnCommand : LocalizedEntityCommands
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;

    public override string Command => "ghostrespawn";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!_cfg.GetCVar(SerenityCCVars.RespawnEnabled))
        {
            shell.WriteError(Loc.GetString("cmd-ghostrespawn-disabled"));
            return;
        }

        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-ghostrespawn-no-player"));
            return;
        }

        if (player.AttachedEntity is not { } uid || !EntityManager.TryGetComponent(uid, out GhostComponent? ghost))
        {
            shell.WriteError(Loc.GetString("cmd-ghostrespawn-not-ghost"));
            return;
        }

        var ready = ghost.TimeOfDeath + TimeSpan.FromSeconds(_cfg.GetCVar(SerenityCCVars.RespawnTime));
        if (_timing.CurTime < ready)
        {
            var seconds = (int) Math.Ceiling((ready - _timing.CurTime).TotalSeconds);
            shell.WriteError(Loc.GetString("cmd-ghostrespawn-too-soon", ("seconds", seconds)));
            return;
        }

        _ticker.Respawn(player);
    }
}
