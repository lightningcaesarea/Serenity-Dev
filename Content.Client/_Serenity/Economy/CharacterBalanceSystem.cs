using Content.Shared._Serenity.Economy;
using Robust.Shared.Timing;

namespace Content.Client._Serenity.Economy;

/// <summary>
/// Keeps the balance of each of the local player's characters, for the lobby's character buttons.
/// </summary>
public sealed partial class CharacterBalanceSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>How long to wait for an answer before asking again.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private Dictionary<int, double> _balances = new();
    private Dictionary<int, double> _startingFunds = new();
    private double? _startingBalance;
    private TimeSpan? _requestedAt;

    /// <summary>Fired whenever new balances arrive.</summary>
    public event Action? BalancesUpdated;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<CharacterBalancesEvent>(OnBalances);
    }

    private void OnBalances(CharacterBalancesEvent ev)
    {
        _balances = ev.Balances;
        _startingFunds = ev.StartingFunds;
        _startingBalance = ev.StartingBalance;
        _requestedAt = null;
        BalancesUpdated?.Invoke();
    }

    /// <summary>
    /// Ask the server for fresh balances. Many buttons ask at once when the character list is rebuilt, so this only
    /// sends while no request is waiting on an answer.
    /// </summary>
    public void RequestBalances()
    {
        var now = _timing.RealTime;
        if (_requestedAt is { } sent && now - sent < RequestTimeout)
            return;

        _requestedAt = now;
        RaiseNetworkEvent(new RequestCharacterBalancesEvent());
    }

    /// <summary>
    /// The balance of the character in <paramref name="slot"/>. A character that has never spawned shows what it
    /// will start with. <paramref name="startingFunds"/> is how much of it is starting funds. False until the server
    /// has answered.
    /// </summary>
    public bool TryGetBalance(int slot, out double balance, out double startingFunds)
    {
        if (_balances.TryGetValue(slot, out balance))
        {
            startingFunds = _startingFunds.GetValueOrDefault(slot);
            return true;
        }

        // Never spawned: it will open with the whole starting balance as starting funds.
        balance = startingFunds = _startingBalance ?? 0;
        return _startingBalance != null;
    }
}
