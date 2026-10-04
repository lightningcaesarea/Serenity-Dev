using System.Threading.Channels;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._NullLink;
using Content.Shared._Serenity.CCVar;
using Content.Shared._Serenity.Economy;
using Content.Shared._Starlight;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Serenity.Economy;

/// <summary>
/// Database-backed replacement for <c>NullLinkPlayerResourcesManager</c>.
/// </summary>
/// <remarks>
/// Starlight stores player resources in NullLink, an external Orleans cluster Serenity has no access to. With
/// NullLink disabled the stock manager keeps balances in memory only and every write silently succeeds, so money
/// evaporates on disconnect.
///
/// This manager keeps the same in-memory <see cref="PlayerData.Resources"/> cache the rest of the economy reads
/// from, but persists it, and records every change in an append-only transaction table for admin disputes.
///
/// Federal Bills (key <c>"credits"</c>) belong to the character, not the account: the cached value is the balance
/// of the character the player spawned as this round, loaded when they spawn, and absent (so nothing can spend or
/// pay it) while they aren't playing one of their characters. Part of a character's balance may still be its
/// starting funds, which spending uses up first and which can't be transferred; see <see cref="TryWithdraw"/>.
/// Any other resource is account-wide: loaded when the player connects, with changes made before the load lands
/// merged on top.
///
/// Every DB operation goes through one ordered queue, so a character's balance is never read back while one of its
/// own writes is still waiting to be flushed, and character rows never have two writers at once.
/// </remarks>
public sealed partial class SerenityPlayerResourcesManager : SharedNullLinkPlayerResourcesManager,
    ISerenityPlayerResourcesManager, ICharacterBalanceManager, IPostInjectInit
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private ISharedPlayersRoleManager _playersRole = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private const string Credits = "credits";

    private bool _initialized;

    // The server entry point never calls Initialize() on this interface (only the client does),
    // so hook IoC's post-injection callback to guarantee we start.
    void IPostInjectInit.PostInject() => Initialize();

    private readonly Channel<Func<Task>> _ops = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    /// <summary>Players whose account-wide resources have been loaded and merged; only these have writes persisted.</summary>
    private readonly HashSet<Guid> _loaded = new();

    /// <summary>
    /// The character each player is playing this round. Kept through disconnects so a player who reconnects into
    /// their body gets the same character's money back.
    /// </summary>
    private readonly Dictionary<NetUserId, ActiveCharacter> _active = new();

    /// <param name="ProfileId">The character's DB id; null until its balance has loaded.</param>
    /// <param name="Load">Which load this is, so a stale one finishing late is ignored.</param>
    /// <param name="StartingFunds">How much of the cached balance is still starting funds.</param>
    private readonly record struct ActiveCharacter(int Slot, int? ProfileId, int Load, double StartingFunds = 0);

    private int _nextLoad;

    private Task? _worker;

    public event Action<ICommonSession>? ActiveBalanceChanged;

    // Read on use: this manager starts during IoC setup, before CVars are registered (e.g. in unit tests).
    public double StartingBalance => _cfg.GetCVar(SerenityCCVars.CharacterStartingBalance);

    public override void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;

        base.Initialize();
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
        // Started from the main thread so continuations resume there and DB calls originate there.
        _worker = ProcessOpsAsync();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        switch (e.NewStatus)
        {
            case SessionStatus.Connected:
                _ = LoadAsync(e.Session);
                // Back mid-round: their PlayerData was rebuilt without the character's money.
                if (_active.TryGetValue(e.Session.UserId, out var active))
                    BeginCharacterLoad(e.Session, active.Slot);
                break;
            case SessionStatus.Disconnected:
                _loaded.Remove(e.Session.UserId);
                break;
        }
    }

    /// <summary>Starlight's PlayerRolesManager creates PlayerData asynchronously after connecting, so wait for it.</summary>
    private async Task<PlayerData?> WaitForPlayerData(ICommonSession session)
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            if (session.Status is SessionStatus.Disconnected or SessionStatus.Zombie)
                return null;

            if (_playersRole.GetPlayerData(session) is { } found)
                return found;

            await Task.Delay(100);
        }

        _sawmill.Error($"Player data for {session.Name} ({session.UserId}) never appeared; resources will not persist this session.");
        return null;
    }

    private async Task LoadAsync(ICommonSession session)
    {
        Dictionary<string, double> stored;
        try
        {
            stored = await _db.GetPlayerResources(session.UserId.UserId);
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Failed to load resources for {session.Name} ({session.UserId}): {ex}");
            return;
        }

        // Account-wide "credits" rows are from before money was per-character, and were reset by that change.
        stored.Remove(Credits);

        if (await WaitForPlayerData(session) is not { } data)
            return;

        // Anything already in memory accrued since connect and has not been persisted yet; merge it on top of the
        // stored value rather than discarding either side.
        foreach (var (resource, value) in stored)
        {
            data.Resources.TryGetValue(resource, out var accrued);
            data.Resources[resource] = value + accrued;
        }

        _loaded.Add(session.UserId);

        // Persist the accrued-during-load deltas exactly once. They go out as deltas, so if an admin changed the
        // stored balance after we read it, that change is kept instead of overwritten.
        foreach (var (resource, value) in data.Resources)
        {
            if (resource == Credits)
                continue;

            stored.TryGetValue(resource, out var before);
            if (value != before)
                EnqueueAdjust(session.UserId, resource, value - before, LedgerReasons.LoadMerge);
        }
    }

    #region Characters

    public void SetActiveCharacter(ICommonSession session, int? slot)
    {
        // Whatever they were playing before, it is not this.
        _playersRole.GetPlayerData(session)?.Resources.Remove(Credits);

        if (slot is not { } s)
        {
            _active.Remove(session.UserId);
            return;
        }

        BeginCharacterLoad(session, s);
    }

    public int? GetActiveSlot(NetUserId user)
        => _active.TryGetValue(user, out var active) ? active.Slot : null;

    public int? GetActiveProfileId(NetUserId user)
        => _active.TryGetValue(user, out var active) ? active.ProfileId : null;

    public double GetStartingFunds(NetUserId user)
        => _active.TryGetValue(user, out var active) && active.ProfileId != null ? active.StartingFunds : 0;

    public void ClearActiveCharacters()
    {
        foreach (var user in _active.Keys)
        {
            if (_players.TryGetSessionById(user, out var session))
                _playersRole.GetPlayerData(session)?.Resources.Remove(Credits);
        }

        _active.Clear();
    }

    private void BeginCharacterLoad(ICommonSession session, int slot)
    {
        var load = ++_nextLoad;
        _active[session.UserId] = new ActiveCharacter(slot, null, load);
        _ = LoadCharacterAsync(session, slot, load);
    }

    private async Task LoadCharacterAsync(ICommonSession session, int slot, int load)
    {
        var user = session.UserId;
        var starting = StartingBalance;

        // Queued behind this character's own pending writes, so it reads what they wrote.
        CharacterAccount? loaded;
        try
        {
            loaded = await Queue(() => _db.EnsureCharacterBalance(user.UserId, slot, starting, LedgerReasons.StartingBalance));
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Failed to load the balance of {session.Name} ({user}) character slot {slot}: {ex}");
            return;
        }

        if (loaded is not { } account)
        {
            _sawmill.Warning($"{session.Name} ({user}) spawned as character slot {slot}, which has no saved character; they have no money this round.");
            return;
        }

        if (await WaitForPlayerData(session) is not { } data)
            return;

        // Respawned as someone else, or the round ended, while this was loading.
        if (!_active.TryGetValue(user, out var active) || active.Load != load)
            return;

        _active[user] = active with { ProfileId = account.ProfileId, StartingFunds = account.StartingFunds };
        data.Resources[Credits] = account.Balance;
        ActiveBalanceChanged?.Invoke(session);
    }

    /// <summary>The character a session's "credits" currently are, if it has loaded.</summary>
    private bool TryGetLoadedCharacter(NetUserId user, out ActiveCharacter character)
    {
        return _active.TryGetValue(user, out character) && character.ProfileId != null;
    }

    public async Task<List<CharacterBalanceSummary>> GetBalancesAsync(NetUserId user)
    {
        // Queued so the played character's pending writes are in before it's read.
        var balances = await Queue(() => _db.GetCharacterBalances(user.UserId));

        // The cache is the truth for the character being played.
        if (TryGetLoadedCharacter(user, out var active)
            && _players.TryGetSessionById(user, out var session)
            && _playersRole.GetPlayerData(session)?.Resources.TryGetValue(Credits, out var live) == true)
        {
            for (var i = 0; i < balances.Count; i++)
            {
                if (balances[i].ProfileId == active.ProfileId)
                    balances[i] = balances[i] with { Balance = live, StartingFunds = active.StartingFunds };
            }
        }

        return balances;
    }

    /// <summary>
    /// The session whose cache holds this character's balance, if it is being played by someone online. A played
    /// character whose player is offline is changed in the DB; their reconnect reloads it behind that write.
    /// </summary>
    private bool TryGetPlayingSession(NetUserId user, int slot, out ICommonSession session)
    {
        session = default!;
        return _active.TryGetValue(user, out var active)
            && active.Slot == slot
            && _players.TryGetSessionById(user, out session!);
    }

    public async Task<double?> AdjustCharacterAsync(NetUserId user, int slot, double delta, string reason)
    {
        if (TryGetPlayingSession(user, slot, out var session))
        {
            // Go through the cache, which is what the game reads. Fails while the balance is still loading.
            if (!UpdateCore(session, Credits, delta, reason))
                return null;

            return TryGetResource(session, Credits, out var after) ? after : null;
        }

        var starting = StartingBalance;
        return await Queue(async () =>
        {
            if (await _db.EnsureCharacterBalance(user.UserId, slot, starting, LedgerReasons.StartingBalance) is not { } account)
                return null;

            var spentStarting = delta < 0 ? -Math.Min(account.StartingFunds, -delta) : 0;
            return (await _db.AdjustCharacterBalance(user.UserId, account.ProfileId, delta, spentStarting, reason))?.Balance;
        });
    }

    public async Task<double?> SetCharacterAsync(NetUserId user, int slot, double value, string reason)
    {
        if (TryGetPlayingSession(user, slot, out var session))
        {
            if (!TryGetLoadedCharacter(user, out _))
                return null;

            // False when it already is that value, which is still a success.
            SetCore(session, Credits, value, reason);
            return TryGetResource(session, Credits, out var after) ? after : null;
        }

        var starting = StartingBalance;
        return await Queue(async () =>
        {
            if (await _db.EnsureCharacterBalance(user.UserId, slot, starting, LedgerReasons.StartingBalance) is not { } account)
                return null;

            return (await _db.SetCharacterBalance(user.UserId, account.ProfileId, value, reason))?.Balance;
        });
    }

    public bool TryWithdraw(ICommonSession session, double amount, string reason, out double fromStartingFunds)
    {
        fromStartingFunds = 0;
        if (amount <= 0
            || !TryGetLoadedCharacter(session.UserId, out var active)
            || !TryGetResource(session, Credits, out var balance)
            || balance < amount)
            return false;

        fromStartingFunds = Math.Min(active.StartingFunds, amount);
        return ApplyCharacterChange(session, -amount, -fromStartingFunds, reason);
    }

    public bool TrySpendEarned(ICommonSession session, double amount, string reason)
    {
        if (amount <= 0
            || !TryGetLoadedCharacter(session.UserId, out var active)
            || !TryGetResource(session, Credits, out var balance)
            || balance - active.StartingFunds < amount)
            return false;

        return ApplyCharacterChange(session, -amount, 0, reason);
    }

    public bool TryDepositStartingFunds(ICommonSession session, double amount, string reason)
        => amount > 0 && ApplyCharacterChange(session, amount, amount, reason);

    #endregion

    #region Starlight interface (no reason available)

    public override bool TryUpdateResource(ICommonSession session, string id, double value, bool skipNullLink = false)
        => UpdateCore(session, id, value, LedgerReasons.Unspecified);

    public override bool TrySetResource(ICommonSession session, string id, double value, bool skipNullLink = false)
        => SetCore(session, id, value, LedgerReasons.Unspecified);

    #endregion

    #region Serenity interface (reason recorded in the ledger)

    public bool TryUpdateResource(ICommonSession session, string id, double delta, string reason)
        => UpdateCore(session, id, delta, reason);

    public bool TryUpdateResource(EntityUid uid, string id, double delta, string reason)
        => _players.TryGetSessionByEntity(uid, out var session) && UpdateCore(session, id, delta, reason);

    public bool TrySetResource(ICommonSession session, string id, double value, string reason)
        => SetCore(session, id, value, reason);

    #endregion

    /// <summary>
    /// Change the played character's balance by <paramref name="delta"/> and its starting funds by
    /// <paramref name="startingFundsDelta"/>, in the cache and (queued) in the DB.
    /// </summary>
    private bool ApplyCharacterChange(ICommonSession session, double delta, double startingFundsDelta, string reason)
    {
        // Match the stock manager: a zero delta is not a change and must not spam the ledger.
        if (delta == 0)
            return false;

        // No character loaded means no account to pay into or out of.
        if (!TryGetLoadedCharacter(session.UserId, out var active) || !base.TryUpdateResource(session, Credits, delta))
            return false;

        TryGetResource(session, Credits, out var balance);
        var startingFunds = Math.Clamp(active.StartingFunds + startingFundsDelta, 0, Math.Max(0, balance ?? 0));
        _active[session.UserId] = active with { StartingFunds = startingFunds };

        var user = session.UserId.UserId;
        var profileId = active.ProfileId!.Value;
        Enqueue(() => _db.AdjustCharacterBalance(user, profileId, delta, startingFundsDelta, reason));
        ActiveBalanceChanged?.Invoke(session);
        return true;
    }

    private bool UpdateCore(ICommonSession session, string id, double delta, string reason)
    {
        if (id == Credits)
        {
            // Ordinary spending uses up the starting funds first; income never adds to them.
            var startingFundsDelta = delta < 0 && TryGetLoadedCharacter(session.UserId, out var active)
                ? -Math.Min(active.StartingFunds, -delta)
                : 0;
            return ApplyCharacterChange(session, delta, startingFundsDelta, reason);
        }

        // Match the stock manager: a zero delta is not a change and must not spam the ledger.
        if (delta == 0 || !base.TryUpdateResource(session, id, delta))
            return false;

        if (_loaded.Contains(session.UserId))
            EnqueueAdjust(session.UserId, id, delta, reason);

        return true;
    }

    private bool SetCore(ICommonSession session, string id, double value, string reason)
    {
        if (_playersRole.GetPlayerData(session) == null)
            return false;

        if (id == Credits)
        {
            if (!TryGetLoadedCharacter(session.UserId, out var active) || !base.TrySetResource(session, id, value))
                return false;

            _active[session.UserId] = active with { StartingFunds = Math.Clamp(active.StartingFunds, 0, Math.Max(0, value)) };

            var user = session.UserId.UserId;
            var profileId = active.ProfileId!.Value;
            Enqueue(() => _db.SetCharacterBalance(user, profileId, value, reason));
            ActiveBalanceChanged?.Invoke(session);
            return true;
        }

        if (!base.TrySetResource(session, id, value))
            return false;

        if (_loaded.Contains(session.UserId))
        {
            var user = session.UserId.UserId;
            Enqueue(() => _db.SetPlayerResource(user, id, value, reason));
        }

        return true;
    }

    private void EnqueueAdjust(NetUserId user, string resource, double delta, string reason)
    {
        var id = user.UserId;
        Enqueue(() => _db.AdjustPlayerResource(id, resource, delta, reason));
    }

    private void Enqueue(Func<Task> op)
    {
        if (!_ops.Writer.TryWrite(op))
            _sawmill.Error("Dropped a resource write: the queue is closed.");
    }

    /// <summary>Run <paramref name="op"/> in order with every queued write and hand back its result.</summary>
    private Task<T> Queue<T>(Func<Task<T>> op)
    {
        var done = new TaskCompletionSource<T>();
        Enqueue(async () =>
        {
            try
            {
                done.SetResult(await op());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
                throw;
            }
        });
        return done.Task;
    }

    private async Task ProcessOpsAsync()
    {
        await foreach (var op in _ops.Reader.ReadAllAsync())
        {
            try
            {
                await op();
            }
            catch (Exception ex)
            {
                // Never let one bad row stall the queue for every other player.
                _sawmill.Error($"Failed to persist a resource change: {ex}");
            }
        }
    }
}
