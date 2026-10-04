using Content.Server.Preferences.Managers;
using Content.Shared._NullLink;
using Content.Shared._Serenity.Economy;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Serenity.Economy;

/// <summary>
/// Ties per-character money to the round: spawning as a character makes its balance the player's
/// <c>"credits"</c>, the round ending clears that, and the lobby can ask for every character's balance.
/// </summary>
public sealed partial class CharacterBalanceSystem : EntitySystem
{
    [Dependency] private ICharacterBalanceManager _balances = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private ISharedNullLinkPlayerResourcesManager _resources = default!;
    [Dependency] private IPlayerManager _players = default!;

    /// <summary>Players who have asked for their balances, so changes to the played character are pushed to them.</summary>
    private readonly Dictionary<NetUserId, Sent> _sent = new();

    private sealed record Sent(Dictionary<int, double> Balances, Dictionary<int, double> StartingFunds);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeNetworkEvent<RequestCharacterBalancesEvent>(OnRequestBalances);

        _balances.ActiveBalanceChanged += OnActiveBalanceChanged;
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _balances.ActiveBalanceChanged -= OnActiveBalanceChanged;
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.Disconnected)
            _sent.Remove(e.Session.UserId);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        // A randomised character isn't one of theirs, so it has no account.
        int? slot = _prefs.GetPreferencesOrNull(ev.Player.UserId) is { } prefs
                    && prefs.TryIndexOfCharacter(ev.Profile, out var index)
            ? index
            : null;

        _balances.SetActiveCharacter(ev.Player, slot);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _balances.ClearActiveCharacters();
    }

    private async void OnRequestBalances(RequestCharacterBalancesEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        try
        {
            var sent = new Sent(new Dictionary<int, double>(), new Dictionary<int, double>());
            foreach (var character in await _balances.GetBalancesAsync(session.UserId))
            {
                if (character.Balance is not { } balance)
                    continue;

                sent.Balances[character.Slot] = balance;
                sent.StartingFunds[character.Slot] = character.StartingFunds;
            }

            if (session.Status == SessionStatus.Disconnected)
                return;

            _sent[session.UserId] = sent;
            Send(session, sent);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to read character balances for {session.Name} ({session.UserId}): {ex}");
        }
    }

    private void OnActiveBalanceChanged(ICommonSession session)
    {
        if (!_sent.TryGetValue(session.UserId, out var sent)
            || _balances.GetActiveSlot(session.UserId) is not { } slot
            || !_resources.TryGetResource(session, "credits", out var credits))
            return;

        sent.Balances[slot] = credits.Value;
        sent.StartingFunds[slot] = _balances.GetStartingFunds(session.UserId);
        Send(session, sent);
    }

    private void Send(ICommonSession session, Sent sent)
        => RaiseNetworkEvent(new CharacterBalancesEvent(
            new Dictionary<int, double>(sent.Balances),
            new Dictionary<int, double>(sent.StartingFunds),
            _balances.StartingBalance), session);
}
