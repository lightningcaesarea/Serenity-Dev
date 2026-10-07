using Content.Server.Chat.Managers;
using Content.Shared._Serenity.Administration;
using Robust.Shared.Network;

namespace Content.Server.Administration.Systems;

/// <summary>
/// Serenity: the ahelp panic button. A confirmed press goes out as an ahelp message from the player,
/// so it reaches admins, the ahelp log and the Discord relay like any other, plus an alert in admin chat.
/// </summary>
public sealed partial class BwoinkSystem
{
    [Dependency] private IChatManager _chatManager = default!;

    private static readonly TimeSpan PanicCooldown = TimeSpan.FromMinutes(2);

    private readonly Dictionary<NetUserId, TimeSpan> _lastPanic = new();

    private void InitializePanic()
    {
        SubscribeNetworkEvent<AHelpPanicButtonEvent>(OnPanicButton);
    }

    private void OnPanicButton(AHelpPanicButtonEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        var now = _timing.RealTime;
        if (_lastPanic.TryGetValue(session.UserId, out var last) && now - last < PanicCooldown)
            return;

        _lastPanic[session.UserId] = now;

        // The round time keeps repeated presses from reading as identical messages to the ahelp spam check.
        var roundTime = _gameTicker.RoundDuration().ToString("hh\\:mm\\:ss");
        var text = Loc.GetString("ahelp-panic-message", ("time", roundTime));

        _sawmill.Info($"{session.Name} ({session.UserId}) pressed the ahelp panic button at {roundTime}.");
        _chatManager.SendAdminAnnouncement(Loc.GetString("ahelp-panic-admin-alert", ("player", session.Name)));

        OnBwoinkTextMessage(new BwoinkTextMessage(session.UserId, session.UserId, text), args);
    }
}
