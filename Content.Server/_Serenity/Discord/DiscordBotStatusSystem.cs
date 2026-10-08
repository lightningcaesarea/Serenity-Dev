using System.Threading.Tasks;
using Content.Server.Discord.DiscordLink;
using Content.Server.GameTicking;
using Content.Shared._Serenity.CCVar;
using Content.Shared.GameTicking;
using NetCord;
using NetCord.Gateway;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._Serenity.Discord;

/// <summary>
/// Keeps the Discord bot's status showing how many players are connected and how far the round is,
/// e.g. "12 players · Round 1:05".
/// </summary>
public sealed partial class DiscordBotStatusSystem : EntitySystem
{
    [Dependency] private DiscordLink _discord = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private GameTicker _ticker = default!;

    // Discord can drop the presence when the bot reconnects, so it is re-sent now and then even when unchanged.
    private static readonly TimeSpan ResendInterval = TimeSpan.FromMinutes(5);

    private bool _enabled;
    private TimeSpan _interval;
    private TimeSpan _nextUpdate;
    private TimeSpan _lastSent;
    private string? _lastText;
    private bool _sending;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, SerenityCCVars.DiscordStatusEnabled, OnEnabledChanged, true);
        Subs.CVar(_cfg, SerenityCCVars.DiscordStatusInterval, v => _interval = TimeSpan.FromSeconds(Math.Max(v, 20f)), true);
    }

    private void OnEnabledChanged(bool enabled)
    {
        _enabled = enabled;
        _lastText = null;
        _nextUpdate = TimeSpan.Zero;

        // Turning it off clears the status rather than leaving a stale count up.
        if (!enabled && _discord.IsConnected)
            _ = SendAsync(null);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled || _sending || !_discord.IsConnected)
            return;

        var now = _timing.RealTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + _interval;

        var text = BuildStatus();
        if (text == _lastText && now - _lastSent < ResendInterval)
            return;

        _lastText = text;
        _lastSent = now;
        _ = SendAsync(text);
    }

    private string BuildStatus()
    {
        var players = Loc.GetString("serenity-discord-status-players", ("count", _players.PlayerCount));
        var duration = _ticker.RoundDuration();
        var time = $"{(int) duration.TotalHours}:{duration.Minutes:D2}";

        var round = _ticker.RunLevel switch
        {
            GameRunLevel.InRound => Loc.GetString("serenity-discord-status-round", ("time", time)),
            GameRunLevel.PostRound => Loc.GetString("serenity-discord-status-round-over"),
            _ => Loc.GetString("serenity-discord-status-lobby"),
        };

        return Loc.GetString("serenity-discord-status", ("players", players), ("round", round));
    }

    private async Task SendAsync(string? text)
    {
        _sending = true;
        try
        {
            var presence = new PresenceProperties(UserStatusType.Online);
            if (text != null)
            {
                presence.Activities =
                [
                    new UserActivityProperties("Custom Status", UserActivityType.Custom) { State = text },
                ];
            }

            await _discord.UpdatePresenceAsync(presence);
        }
        catch (Exception e)
        {
            // Usually the bot is still connecting; the next tick tries again.
            Log.Warning($"Couldn't update the Discord bot status: {e.Message}");
            _lastText = null;
        }
        finally
        {
            _sending = false;
        }
    }
}
