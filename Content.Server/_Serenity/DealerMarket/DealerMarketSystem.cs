using System.Linq;
using Content.Server.Popups;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Server.Stack;
using Content.Shared._Serenity.DealerMarket;
using Content.Shared._Serenity.Economy;
using Content.Shared._Starlight.MassDriver.Components;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Server.Cargo.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Containers;

namespace Content.Server._Serenity.DealerMarket;

/// <summary>
/// The dealer market. Every player is dealt their own short list of contracts from the dealers' templates, paid
/// into their own account. Goods go in by being laid on a linked intake pad (typically landed there by a mass
/// driver) and purchases come back out of a linked mass driver.
/// </summary>
public sealed partial class DealerMarketSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IComponentFactory _compFactory = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private ISerenityPlayerResourcesManager _resources = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedEntityStorageSystem _storage = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    private const int InitialContracts = 3;
    private const int MaxContracts = 4;
    private const int MaxPendingDeliveries = 6;
    private static readonly TimeSpan DealInterval = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan DeclineWait = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DeliveryDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan UiRefresh = TimeSpan.FromSeconds(4);

    private readonly Dictionary<NetUserId, Book> _books = new();
    private readonly List<Delivery> _deliveries = new();
    private TimeSpan _nextUiRefresh;

    private sealed class Book
    {
        public readonly List<Contract> Contracts = new();
        public TimeSpan NextDeal;
        public int NextId = 1;
    }

    private sealed class Contract
    {
        public int Id;
        public ProtoId<DealerPrototype> Dealer;
        public DealerContractTemplate Template = default!;
        public List<int> Counts = new();
        public int Payout;
        public TimeSpan Expires;
        public bool RoleContract;
    }

    private sealed record Delivery(TimeSpan Due, EntityUid Console, ProtoId<DealerPrototype> Dealer, DealerOffer Offer);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DealerMarketConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<DealerMarketConsoleComponent, DealerFulfilMessage>(OnFulfil);
        SubscribeLocalEvent<DealerMarketConsoleComponent, DealerDeclineMessage>(OnDecline);
        SubscribeLocalEvent<DealerMarketConsoleComponent, DealerRequestCrateMessage>(OnRequestCrate);
        SubscribeLocalEvent<DealerMarketConsoleComponent, DealerBuyMessage>(OnBuy);
        SubscribeLocalEvent<DealerMarketConsoleComponent, DealerSellMessage>(OnSell);
        SubscribeLocalEvent<DealerMarketConsoleComponent, NewLinkEvent>(OnNewLink);
        SubscribeLocalEvent<DealerMarketConsoleComponent, PortDisconnectedEvent>(OnPortDisconnected);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _books.Clear();
            _deliveries.Clear();
        });
    }

    #region Linking

    private void OnNewLink(Entity<DealerMarketConsoleComponent> ent, ref NewLinkEvent args)
    {
        if (args.SourcePort != ent.Comp.LinkingPort)
            return;

        if (HasComp<DealerIntakeComponent>(args.Sink))
            ent.Comp.Intakes.Add(args.Sink);
        else if (HasComp<DealerElevatorComponent>(args.Sink))
            ent.Comp.Elevators.Add(args.Sink);
        else if (HasComp<MassDriverComponent>(args.Sink))
            ent.Comp.Outlets.Add(args.Sink);
    }

    private void OnPortDisconnected(Entity<DealerMarketConsoleComponent> ent, ref PortDisconnectedEvent args)
    {
        if (args.Port != ent.Comp.LinkingPort)
            return;

        ent.Comp.Intakes.Remove(args.Sink);
        ent.Comp.Elevators.Remove(args.Sink);
        ent.Comp.Outlets.Remove(args.Sink);
    }

    private EntityUid? FirstValid(List<EntityUid> list)
    {
        foreach (var uid in list)
        {
            if (Exists(uid) && !Terminating(uid))
                return uid;
        }

        return null;
    }

    #endregion

    #region Update

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        for (var i = _deliveries.Count - 1; i >= 0; i--)
        {
            var delivery = _deliveries[i];
            if (delivery.Due > now)
                continue;

            _deliveries.RemoveAt(i);
            Deliver(delivery);
        }

        if (now < _nextUiRefresh)
            return;

        _nextUiRefresh = now + UiRefresh;
        SweepElevators();
        var query = EntityQueryEnumerator<DealerMarketConsoleComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out var console, out _))
        {
            foreach (var actor in _ui.GetActors(uid, DealerMarketUiKey.Key).ToList())
            {
                SendState((uid, console), actor);
            }
        }
    }

    /// <summary>Take down crates whose contract is gone, and forget crates that were destroyed.</summary>
    private void SweepElevators()
    {
        var query = EntityQueryEnumerator<DealerElevatorComponent>();
        while (query.MoveNext(out var uid, out var elevator))
        {
            if (elevator.ActiveCrate is not { } crate)
                continue;

            if (!TryComp(crate, out DealerContractCrateComponent? marker) || Terminating(crate))
            {
                elevator.ActiveCrate = null;
                _appearance.SetData(uid, DealerElevatorVisuals.Raised, false);
                continue;
            }

            if (!_books.TryGetValue(marker.Owner, out var book)
                || book.Contracts.All(c => c.Id != marker.ContractId || c.Expires <= _timing.CurTime))
            {
                Retract(uid, elevator);
            }
        }
    }

    /// <summary>The elevator takes its crate back down; anything left inside is tipped out onto the platform.</summary>
    private void Retract(EntityUid uid, DealerElevatorComponent elevator)
    {
        if (elevator.ActiveCrate is { } crate && Exists(crate) && !Terminating(crate))
        {
            _storage.OpenStorage(crate);
            QueueDel(crate);
        }

        elevator.ActiveCrate = null;
        _appearance.SetData(uid, DealerElevatorVisuals.Raised, false);
    }

    private void Deliver(Delivery delivery)
    {
        if (!TryComp(delivery.Console, out DealerMarketConsoleComponent? console) || Terminating(delivery.Console))
            return;

        // The mass driver that carries the goods out; a console with its outlet taken apart drops them at its own feet.
        var origin = FirstValid(console.Outlets) ?? delivery.Console;
        var coords = Transform(origin).Coordinates;
        var proto = _proto.Index(delivery.Offer.Item);

        if (proto.TryGetComponent<StackComponent>(out _, _compFactory))
        {
            _stack.SpawnMultipleAtPosition(delivery.Offer.Item, delivery.Offer.Amount, coords);
        }
        else
        {
            for (var n = 0; n < delivery.Offer.Amount; n++)
            {
                Spawn(delivery.Offer.Item, coords);
            }
        }

        _popup.PopupEntity(Loc.GetString("dealer-market-delivered", ("item", proto.Name)), origin, PopupType.Medium);
    }

    #endregion

    #region Contracts

    private Book GetBook(ICommonSession session)
    {
        if (_books.TryGetValue(session.UserId, out var book))
            return book;

        book = new Book();
        _books[session.UserId] = book;
        for (var i = 0; i < InitialContracts; i++)
        {
            DealContract(session, book);
        }

        book.NextDeal = _timing.CurTime + DealInterval;
        return book;
    }

    /// <summary>Drop what has run out, and deal a new one if it is time for it.</summary>
    private void Refresh(ICommonSession session, Book book)
    {
        var now = _timing.CurTime;
        book.Contracts.RemoveAll(c => c.Expires <= now);

        while (book.Contracts.Count < MaxContracts && now >= book.NextDeal)
        {
            if (!DealContract(session, book))
                break;

            book.NextDeal += DealInterval;
        }

        // Don't let the next deal queue up a backlog while the book is full.
        if (book.Contracts.Count >= MaxContracts && book.NextDeal < now)
            book.NextDeal = now + DealInterval;
    }

    private string? PlayerJob(ICommonSession session)
    {
        if (!_mind.TryGetMind(session, out var mindId, out _))
            return null;

        return _jobs.MindTryGetJobId(mindId, out var job) ? job?.Id : null;
    }

    private bool DealContract(ICommonSession session, Book book)
    {
        var job = PlayerJob(session);
        var options = new List<(ProtoId<DealerPrototype> Dealer, DealerContractTemplate Template)>();

        foreach (var dealer in _proto.EnumeratePrototypes<DealerPrototype>())
        {
            foreach (var template in dealer.Contracts)
            {
                if (template.Jobs.Count > 0 && (job == null || !template.Jobs.Any(j => j.Id == job)))
                    continue;

                // Never hold two of the same job at once.
                if (book.Contracts.Any(c => c.Template == template))
                    continue;

                options.Add((dealer.ID, template));
            }
        }

        if (options.Count == 0)
            return false;

        // A player's own role contracts are worth taking: they weigh in twice.
        var weighted = options.SelectMany(o => o.Template.Jobs.Count > 0 ? new[] { o, o } : new[] { o }).ToList();
        var (dealerId, chosen) = _random.Pick(weighted);

        var counts = new List<int>();
        var payout = chosen.Bonus;
        foreach (var want in chosen.Wants)
        {
            var count = _random.Next(Math.Min(want.Min, want.Max), Math.Max(want.Min, want.Max) + 1);
            counts.Add(count);
            payout += count * want.UnitPrice;
        }

        book.Contracts.Add(new Contract
        {
            Id = book.NextId++,
            Dealer = dealerId,
            Template = chosen,
            Counts = counts,
            Payout = payout,
            Expires = _timing.CurTime + TimeSpan.FromMinutes(chosen.LifetimeMinutes),
            RoleContract = chosen.Jobs.Count > 0,
        });
        return true;
    }

    private void OnFulfil(Entity<DealerMarketConsoleComponent> ent, ref DealerFulfilMessage args)
    {
        if (!_players.TryGetSessionByEntity(args.Actor, out var session))
            return;

        var book = GetBook(session);
        Refresh(session, book);

        var contractId = args.Id;
        var contract = book.Contracts.FirstOrDefault(c => c.Id == contractId);
        if (contract == null)
        {
            _popup.PopupEntity(Loc.GetString("dealer-market-contract-gone"), ent, args.Actor, PopupType.SmallCaution);
            SendState(ent, args.Actor);
            return;
        }

        if (CrateFor(ent.Comp, session, contract.Id) is not { } elevator)
        {
            _popup.PopupEntity(Loc.GetString("dealer-market-no-crate"), ent, args.Actor, PopupType.SmallCaution);
            SendState(ent, args.Actor);
            return;
        }

        var pad = CrateContents(elevator.Comp.ActiveCrate!.Value);
        var take = new List<(EntityUid Entity, int Count)>();
        for (var i = 0; i < contract.Template.Wants.Count; i++)
        {
            var need = contract.Counts[i];
            var want = contract.Template.Wants[i].Item;
            foreach (var item in pad)
            {
                if (need <= 0)
                    break;

                if (!Matches(item, want) || take.Any(t => t.Entity == item))
                    continue;

                var have = _stack.GetCount(item);
                var used = Math.Min(have, need);
                take.Add((item, used));
                need -= used;
            }

            if (need > 0)
            {
                _popup.PopupEntity(Loc.GetString("dealer-market-contract-short"), ent, args.Actor, PopupType.SmallCaution);
                SendState(ent, args.Actor);
                return;
            }
        }

        foreach (var (item, used) in take)
        {
            Consume(item, used);
        }

        Retract(elevator.Owner, elevator.Comp);

        book.Contracts.Remove(contract);
        _resources.TryUpdateResource(session, "credits", contract.Payout, LedgerReasons.DealerContract(contract.Dealer));
        _popup.PopupEntity(Loc.GetString("dealer-market-contract-paid",
            ("dealer", _proto.Index(contract.Dealer).Name), ("payout", contract.Payout)), ent, args.Actor, PopupType.Medium);
        SendState(ent, args.Actor);
    }

    private void OnRequestCrate(Entity<DealerMarketConsoleComponent> ent, ref DealerRequestCrateMessage args)
    {
        if (!_players.TryGetSessionByEntity(args.Actor, out var session))
            return;

        var book = GetBook(session);
        Refresh(session, book);

        var contractId = args.Id;
        var contract = book.Contracts.FirstOrDefault(c => c.Id == contractId);
        if (contract == null)
        {
            _popup.PopupEntity(Loc.GetString("dealer-market-contract-gone"), ent, args.Actor, PopupType.SmallCaution);
            SendState(ent, args.Actor);
            return;
        }

        if (FirstValid(ent.Comp.Elevators) is not { } elevatorUid || !TryComp(elevatorUid, out DealerElevatorComponent? elevator))
        {
            _popup.PopupEntity(Loc.GetString("dealer-market-no-elevator"), ent, args.Actor, PopupType.SmallCaution);
            return;
        }

        if (CrateFor(ent.Comp, session, contract.Id) != null)
        {
            _popup.PopupEntity(Loc.GetString("dealer-market-crate-already"), ent, args.Actor, PopupType.SmallCaution);
            return;
        }

        // One crate at a time: whatever was up for another contract goes back down, its contents tipped out.
        Retract(elevatorUid, elevator);

        var crate = Spawn(elevator.Crate, Transform(elevatorUid).Coordinates);
        var marker = EnsureComp<DealerContractCrateComponent>(crate);
        marker.ContractId = contract.Id;
        marker.Owner = session.UserId;
        _metaData.SetEntityName(crate, Loc.GetString("dealer-market-crate-name", ("title", contract.Template.Title)));
        elevator.ActiveCrate = crate;
        _appearance.SetData(elevatorUid, DealerElevatorVisuals.Raised, true);
        _popup.PopupEntity(Loc.GetString("dealer-market-crate-up"), elevatorUid, PopupType.Medium);
        SendState(ent, args.Actor);
    }

    /// <summary>The elevator that currently has this player's crate for this contract up, if any.</summary>
    private Entity<DealerElevatorComponent>? CrateFor(DealerMarketConsoleComponent console, ICommonSession session, int contractId)
    {
        foreach (var uid in console.Elevators)
        {
            if (!Exists(uid)
                || !TryComp(uid, out DealerElevatorComponent? elevator)
                || elevator.ActiveCrate is not { } crate
                || !TryComp(crate, out DealerContractCrateComponent? marker)
                || Terminating(crate))
                continue;

            if (marker.Owner == session.UserId && marker.ContractId == contractId)
                return (uid, elevator);
        }

        return null;
    }

    /// <summary>Everything inside a crate, including what is in boxes inside it.</summary>
    private List<EntityUid> CrateContents(EntityUid crate)
    {
        var found = new List<EntityUid>();
        if (!TryComp(crate, out EntityStorageComponent? storage))
            return found;

        var queue = new Queue<EntityUid>(storage.Contents.ContainedEntities);
        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            found.Add(item);
            foreach (var container in _container.GetAllContainers(item))
            {
                foreach (var inner in container.ContainedEntities)
                {
                    queue.Enqueue(inner);
                }
            }
        }

        return found;
    }

    private void OnDecline(Entity<DealerMarketConsoleComponent> ent, ref DealerDeclineMessage args)
    {
        if (!_players.TryGetSessionByEntity(args.Actor, out var session))
            return;

        var book = GetBook(session);
        var contractId = args.Id;
        if (book.Contracts.RemoveAll(c => c.Id == contractId) > 0)
            book.NextDeal = _timing.CurTime + DeclineWait;

        SendState(ent, args.Actor);
    }

    #endregion

    #region Goods

    private bool Matches(EntityUid item, EntProtoId want)
    {
        if (MetaData(item).EntityPrototype?.ID == want.Id)
            return true;

        if (!TryComp(item, out StackComponent? stack))
            return false;

        return _proto.Index(want).TryGetComponent<StackComponent>(out var wanted, _compFactory)
               && wanted.StackTypeId == stack.StackTypeId;
    }

    private void Consume(EntityUid item, int count)
    {
        if (HasComp<StackComponent>(item))
        {
            _stack.SetCount(item, _stack.GetCount(item) - count);
            return;
        }

        QueueDel(item);
    }

    /// <summary>Everything loose on the console's linked pads.</summary>
    private List<EntityUid> PadEntities(DealerMarketConsoleComponent console)
    {
        var found = new HashSet<EntityUid>();
        foreach (var pad in console.Intakes)
        {
            if (!Exists(pad) || Terminating(pad) || !TryComp(pad, out DealerIntakeComponent? intake))
                continue;

            foreach (var uid in _lookup.GetEntitiesInRange(Transform(pad).Coordinates, intake.Range, LookupFlags.Dynamic | LookupFlags.Sundries))
            {
                if (uid == pad
                    || Transform(uid).Anchored
                    || _container.IsEntityInContainer(uid)
                    || !TryComp(uid, out PhysicsComponent? physics)
                    || physics.BodyType == Robust.Shared.Physics.BodyType.Static)
                    continue;

                found.Add(uid);
            }
        }

        return found.ToList();
    }

    private void OnBuy(Entity<DealerMarketConsoleComponent> ent, ref DealerBuyMessage args)
    {
        if (!_players.TryGetSessionByEntity(args.Actor, out var session)
            || !_proto.TryIndex(args.Dealer, out var dealer)
            || args.Offer < 0
            || args.Offer >= dealer.Stock.Count)
            return;

        var offer = dealer.Stock[args.Offer];

        if (FirstValid(ent.Comp.Outlets) == null)
        {
            _popup.PopupEntity(Loc.GetString("dealer-market-no-outlet"), ent, args.Actor, PopupType.SmallCaution);
            return;
        }

        var console = ent.Owner;
        if (_deliveries.Count(d => d.Console == console) >= MaxPendingDeliveries)
        {
            _popup.PopupEntity(Loc.GetString("dealer-market-backed-up"), ent, args.Actor, PopupType.SmallCaution);
            return;
        }

        if (!_resources.TryGetResource(session, "credits", out var balance) || balance < offer.Price)
        {
            _popup.PopupEntity(Loc.GetString("dealer-market-cant-afford"), ent, args.Actor, PopupType.SmallCaution);
            return;
        }

        _resources.TryUpdateResource(session, "credits", -offer.Price, LedgerReasons.DealerPurchase(dealer.ID));
        _deliveries.Add(new Delivery(_timing.CurTime + DeliveryDelay, ent, dealer.ID, offer));
        _popup.PopupEntity(Loc.GetString("dealer-market-ordered", ("dealer", dealer.Name)), ent, args.Actor, PopupType.Medium);
        SendState(ent, args.Actor);
    }

    private void OnSell(Entity<DealerMarketConsoleComponent> ent, ref DealerSellMessage args)
    {
        if (!_players.TryGetSessionByEntity(args.Actor, out var session)
            || !_proto.TryIndex(args.Dealer, out var dealer)
            || dealer.Buys == null)
            return;

        var total = 0;
        var sold = new List<EntityUid>();
        foreach (var item in PadEntities(ent.Comp))
        {
            if (!_whitelist.IsValid(dealer.Buys, item))
                continue;

            total += (int) (_pricing.GetPrice(item, includeContents: false) * dealer.BuyRate);
            sold.Add(item);
        }

        if (total <= 0)
        {
            _popup.PopupEntity(Loc.GetString("dealer-market-nothing-to-sell", ("dealer", dealer.Name)), ent, args.Actor, PopupType.SmallCaution);
            return;
        }

        foreach (var item in sold)
        {
            QueueDel(item);
        }

        _resources.TryUpdateResource(session, "credits", total, LedgerReasons.DealerSale(dealer.ID));
        _popup.PopupEntity(Loc.GetString("dealer-market-sold", ("dealer", dealer.Name), ("payout", total)), ent, args.Actor, PopupType.Medium);
        SendState(ent, args.Actor);
    }

    #endregion

    #region UI

    private void OnOpened(Entity<DealerMarketConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        SendState(ent, args.Actor);
    }

    private void SendState(Entity<DealerMarketConsoleComponent> ent, EntityUid actor)
    {
        if (!_players.TryGetSessionByEntity(actor, out var session))
            return;

        var book = GetBook(session);
        Refresh(session, book);

        var pad = PadEntities(ent.Comp);
        var hasIntake = FirstValid(ent.Comp.Intakes) != null;

        var contracts = new List<DealerContractInfo>();
        foreach (var contract in book.Contracts)
        {
            var wants = new List<DealerContractWantInfo>();
            var crate = CrateFor(ent.Comp, session, contract.Id);
            var inCrate = crate?.Comp.ActiveCrate is { } c ? CrateContents(c) : new List<EntityUid>();
            var ready = crate != null;
            for (var i = 0; i < contract.Template.Wants.Count; i++)
            {
                var item = contract.Template.Wants[i].Item;
                var have = inCrate.Where(p => Matches(p, item)).Sum(p => _stack.GetCount(p));
                ready &= have >= contract.Counts[i];
                wants.Add(new DealerContractWantInfo(_proto.Index(item).Name, contract.Counts[i], have));
            }

            contracts.Add(new DealerContractInfo(
                contract.Id,
                contract.Dealer,
                contract.Template.Title,
                contract.Template.Flavour,
                wants,
                contract.Payout,
                (int) Math.Max(0, (contract.Expires - _timing.CurTime).TotalSeconds),
                crate != null,
                ready,
                contract.RoleContract));
        }

        var padList = pad
            .GroupBy(p => MetaData(p).EntityName)
            .Select(g => new DealerPadItem(g.Key, g.Sum(p => _stack.GetCount(p))))
            .OrderBy(i => i.Name)
            .ToList();

        var quotes = new Dictionary<ProtoId<DealerPrototype>, int>();
        foreach (var dealer in _proto.EnumeratePrototypes<DealerPrototype>())
        {
            if (dealer.Buys == null)
                continue;

            var total = 0;
            foreach (var item in pad)
            {
                if (_whitelist.IsValid(dealer.Buys, item))
                    total += (int) (_pricing.GetPrice(item, includeContents: false) * dealer.BuyRate);
            }

            quotes[dealer.ID] = total;
        }

        _resources.TryGetResource(session, "credits", out var balance);

        _ui.ServerSendUiMessage(ent.Owner, DealerMarketUiKey.Key, new DealerMarketStateMessage(
            (int) (balance ?? 0),
            hasIntake,
            FirstValid(ent.Comp.Outlets) != null,
            FirstValid(ent.Comp.Elevators) != null,
            _deliveries.Count(d => d.Console == ent.Owner),
            contracts,
            padList,
            quotes), actor);
    }

    #endregion
}
