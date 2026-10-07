using Content.Server.Chat.Managers;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Serenity.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Radio;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._Serenity.Welcome;

/// <summary>
/// Greets players with little playtime when they spawn: a private welcome in their chat, and a radio call from the
/// station so others know someone new has arrived and can help them out.
/// </summary>
public sealed class NewPlayerWelcomeSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private PlayTimeTrackingManager _playTime = default!;
    [Dependency] private RadioSystem _radio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!_cfg.GetCVar(SerenityCCVars.NewPlayerWelcomeEnabled))
            return;

        TimeSpan playtime;
        try
        {
            playtime = _playTime.GetOverallPlaytime(args.Player);
        }
        catch (InvalidOperationException)
        {
            return; // Playtime not loaded yet, so we can't tell whether they're new.
        }

        if (playtime >= TimeSpan.FromMinutes(_cfg.GetCVar(SerenityCCVars.NewPlayerWelcomeMaxPlaytime)))
            return;

        _chat.DispatchServerMessage(args.Player, Loc.GetString("new-player-welcome-message"));

        var channel = _cfg.GetCVar(SerenityCCVars.NewPlayerWelcomeChannel);
        if (string.IsNullOrEmpty(channel)
            || !Exists(args.Station)
            || !_proto.TryIndex<RadioChannelPrototype>(channel, out var channelProto))
            return;

        _radio.SendRadioMessage(args.Station,
            Loc.GetString("new-player-welcome-radio", ("character", Name(args.Mob))),
            channelProto,
            args.Station);
    }
}
