using Content.Server.Hands.Systems;
using Content.Server.Stack;
using Content.Shared.Cargo.Components;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Server.Player;
using Robust.Shared.Player;
using Content.Server.Mind;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared._NullLink;
using Content.Server._Serenity.Economy; // Serenity
using Content.Shared._Serenity.Economy; // Serenity
using Content.Shared._Starlight.Economy.Atm;
using Content.Shared.Popups; // Serenity

namespace Content.Server._Starlight.Economy.Atm;
public sealed partial class ATMSystem : SharedATMSystem
{
    [Dependency] private IPlayerRolesManager _playerRolesManager = default!;
    [Dependency] private Content.Shared._Serenity.Economy.ISerenityPlayerResourcesManager _playerResources = default!; // Serenity: reason-carrying variant
    [Dependency] private ICharacterBalanceManager _characterBalances = default!; // Serenity: starting funds
    [Dependency] private BoundCashSystem _boundCash = default!; // Serenity
    [Dependency] private SharedPopupSystem _popup = default!; // Serenity
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;

    private static readonly EntProtoId<StackComponent> _cash = "SpaceCash"; // Serenity: was NTCredit
    private static readonly EntProtoId<StackComponent> _boundCashProto = "SpaceCashBound"; // Serenity: withdrawn starting funds
    private readonly object _transferLock = new();
    public override void Initialize()
    {
        SubscribeLocalEvent<ATMComponent, BeforeActivatableUIOpenEvent>(OnBeforeActivatableUIOpen);
        SubscribeLocalEvent<CashComponent, AfterInteractEvent>(OnAfterInteract); // Serenity: was NTCashComponent
        Subs.BuiEvents<ATMComponent>(ATMUIKey.Key, subs =>
        {
            subs.Event<ATMWithdrawBuiMsg>(OnWithdraw);
            subs.Event<ATMTransferBuiMsg>(OnTransfer);
        });
        base.Initialize();
    }

    /// <summary>Serenity: the UI state for <paramref name="actor"/>, including how much of their balance is starting funds.</summary>
    private ATMBuiState State(EntityUid actor, string? message = null, bool isError = false)
    {
        _playerResources.TryGetResource(actor, "credits", out var balance);
        var startingFunds = _players.TryGetSessionByEntity(actor, out var session)
            ? _characterBalances.GetStartingFunds(session.UserId)
            : 0;

        return new ATMBuiState
        {
            Balance = (int?) balance ?? 0,
            StartingFunds = (int) startingFunds,
            Message = message,
            IsError = isError,
        };
    }

    private void OnWithdraw(EntityUid uid, ATMComponent component, ATMWithdrawBuiMsg args)
    {
        if (!_players.TryGetSessionByEntity(args.Actor, out var actorSession))
            return;

        // Serenity: starting funds come out first, as bills only this character can use.
        if (!_characterBalances.TryWithdraw(actorSession, args.Amount, LedgerReasons.AtmWithdraw, out var fromStartingFunds))
            return;

        var bound = (int) fromStartingFunds;
        var free = args.Amount - bound;

        if (bound > 0 && _characterBalances.GetActiveProfileId(actorSession.UserId) is { } profileId)
        {
            var ownerName = _mind.TryGetMind(actorSession.UserId, out _, out var mind) && mind.CharacterName is { } name
                ? name
                : Name(args.Actor);
            var boundCash = SpawnCash(uid, _boundCashProto, bound);
            _boundCash.Bind(boundCash, profileId, args.Actor, ownerName);
            _hands.TryPickup(args.Actor, boundCash);
        }

        if (free > 0)
            _hands.TryPickup(args.Actor, SpawnCash(uid, _cash, free));

        var state = State(args.Actor);
        _uiSystem.SetUiState(uid, ATMUIKey.Key, state);
        _audioSystem.PlayPvs(component.WithdrawSound, uid);

        _adminLogger.Add(LogType.Economy, LogImpact.Low,
            $"{ToPrettyString(args.Actor):player} withdrew {args.Amount} Federal Bills ({bound} of them starting funds) at {ToPrettyString(uid):entity} (balance {state.Balance})"); // Serenity
    }

    private EntityUid SpawnCash(EntityUid atm, EntProtoId<StackComponent> proto, int amount)
    {
        var cash = SpawnAtPosition(proto, Transform(atm).Coordinates);
        var stack = EnsureComp<StackComponent>(cash);
        _stack.SetCount((cash, stack), amount);
        return cash;
    }

    private void OnAfterInteract(Entity<CashComponent> ent, ref AfterInteractEvent args)
    {
        if (!TryComp<StackComponent>(ent.Owner, out var stack)
            || !args.Target.HasValue
            || !TryComp<ATMComponent>(args.Target, out var atm)
            || !_players.TryGetSessionByEntity(args.User, out var userSession)
            || !_playerResources.TryGetResource(args.User, "credits", out _))
            return;

        // Serenity: bound bills go back in as starting funds, and only for their owner.
        if (TryComp<BoundCashComponent>(ent, out var bound))
        {
            args.Handled = true;
            if (!_boundCash.IsOwner((ent.Owner, bound), args.User)
                || !_characterBalances.TryDepositStartingFunds(userSession, stack.Count, LedgerReasons.AtmDeposit))
            {
                _popup.PopupEntity(Loc.GetString("bound-cash-not-yours"), args.Target.Value, args.User);
                return;
            }
        }
        else if (stack.StackTypeId == "Credit") // Serenity: only Federal Bills (SpaceCash) deposit; ignore other cash-flagged stacks
        {
            args.Handled = true; // If we don't do this - debug assert and crash at the dev build.
            // Serenity: no deposit fee.
            _playerResources.TryUpdateResource(userSession, "credits", stack.Count, LedgerReasons.AtmDeposit); // Serenity
        }
        else
        {
            return;
        }

        var diff = stack.Count;
        QueueDel(ent);
        var state = State(args.User);
        _uiSystem.SetUiState(args.Target.Value, ATMUIKey.Key, state);
        _audioSystem.PlayPvs(atm.DepositSound, args.Target.Value);

        _adminLogger.Add(LogType.Economy, LogImpact.Low,
            $"{ToPrettyString(args.User):player} deposited {diff} {(bound != null ? "bound " : "")}Federal Bills at {ToPrettyString(args.Target.Value):entity} (balance {state.Balance})"); // Serenity
    }

    private void OnTransfer(EntityUid uid, ATMComponent component, ATMTransferBuiMsg args)
    {
        if (!_playerResources.TryGetResource(args.Actor, "credits", out var balance) || balance < args.Amount || args.Amount < 0)
            return;

        if (string.IsNullOrWhiteSpace(args.Recipient))
        {
            _uiSystem.SetUiState(uid, ATMUIKey.Key, State(args.Actor, Loc.GetString("economy-atm-transfer-error-no-recipient"), true));
            return;
        }

        if (!_players.TryGetSessionByEntity(args.Actor, out var senderSession))
        {
            _uiSystem.SetUiState(uid, ATMUIKey.Key, State(args.Actor, Loc.GetString("economy-atm-transfer-error-generic"), true));
            return;
        }

        // Serenity: starting funds stay with the character they were issued to.
        var transferable = balance - _characterBalances.GetStartingFunds(senderSession.UserId);
        if (transferable < args.Amount)
        {
            _uiSystem.SetUiState(uid, ATMUIKey.Key, State(args.Actor,
                Loc.GetString("economy-atm-transfer-error-starting-funds", ("max", (int) Math.Max(0, transferable ?? 0))), true));
            return;
        }

        var matches = new List<ICommonSession>();

        foreach (var reg in _playerRolesManager.Players)
        {
            if (_mind.TryGetMind(reg.Session.UserId, out _, out var mind)
                && !string.IsNullOrWhiteSpace(mind.CharacterName)
                && string.Equals(mind.CharacterName, args.Recipient, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(reg.Session);
            }
        }

        if (matches.Count != 1)
        {
            var key = matches.Count == 0
                ? "economy-atm-transfer-error-no-recipient"
                : "economy-atm-transfer-error-ambiguous";

            _uiSystem.SetUiState(uid, ATMUIKey.Key, State(args.Actor, Loc.GetString(key), true));
            return;
        }

        var recipientSession = matches[0];

        if (recipientSession.UserId == senderSession.UserId)
        {
            _uiSystem.SetUiState(uid, ATMUIKey.Key, State(args.Actor, Loc.GetString("economy-atm-transfer-error-self"), true));
            return;
        }

        lock (_transferLock)
        {
            if (!_playerResources.TryGetResource(recipientSession, "credits", out _))
            {
                _uiSystem.SetUiState(uid, ATMUIKey.Key, State(args.Actor, Loc.GetString("economy-atm-transfer-error-no-recipient"), true));
                return;
            }

            // Serenity: debit earned money first, and only pay out if that worked; each side names the other, so the
            // two ledger rows of a transfer can be matched up
            if (!_characterBalances.TrySpendEarned(senderSession, args.Amount, LedgerReasons.TransferOut(recipientSession.UserId.UserId)))
            {
                _uiSystem.SetUiState(uid, ATMUIKey.Key, State(args.Actor, Loc.GetString("economy-atm-transfer-error-generic"), true));
                return;
            }

            _playerResources.TryUpdateResource(recipientSession, "credits", args.Amount, LedgerReasons.TransferIn(senderSession.UserId.UserId));

            var recipientName = _mind.TryGetMind(recipientSession.UserId, out _, out var rMind)
                ? rMind.CharacterName ?? recipientSession.Name
                : recipientSession.Name;

            _uiSystem.SetUiState(uid, ATMUIKey.Key, State(args.Actor,
                Loc.GetString("economy-atm-transfer-success", ("amount", args.Amount), ("recipient", recipientName))));

            _adminLogger.Add(
                LogType.Action,
                LogImpact.Medium,
                $"{ToPrettyString(args.Actor):player} transferred {args.Amount} cr. to {recipientName} via {ToPrettyString(uid):entity}");
        }
    }

    private void OnBeforeActivatableUIOpen(Entity<ATMComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        _uiSystem.SetUiState(ent.Owner, ATMUIKey.Key, State(args.User));
    }
}
