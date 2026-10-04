// Ported from Frontier Station 14 at commit 5be37d18c2 (2024-07-01), MIT licensed.
// Copyright (c) 2017-2024 New Frontiers. Adapted for Serenity: buyers pay with Federal Bills (SpaceCash)
// inserted into the console's bill slot; sale proceeds are spawned back at the console.

using System.Linq;
using System.Text.RegularExpressions;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Server.Radio.EntitySystems;
using Content.Server.Stack;
using Content.Server.StationRecords.Systems;
using Content.Server._Serenity.Economy;
using Content.Shared._Serenity.Economy;
using Content.Shared._Serenity.Shipyard;
using Content.Shared._Serenity.Shipyard.BUI;
using Content.Shared._Serenity.Shipyard.Components;
using Content.Shared._Serenity.Shipyard.Events;
using Content.Shared._Serenity.Shipyard.Prototypes;
using Content.Shared.Shuttles.Components;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Components;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.StationRecords;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Serenity.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    private static readonly Regex ParentheticalRegex = new(@"\s*\([^()]*\)", RegexOptions.Compiled);

    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private SharedAccessSystem _access = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private BoundCashSystem _boundCash = default!; // Serenity: starting funds stay bound through a ship sale
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedCargoSystem _cargo = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private StationRecordsSystem _records = default!;
    [Dependency] private StackSystem _stackSystem = default!;

    private void InitializeConsole()
    {
        SubscribeLocalEvent<ShipyardConsoleComponent, BoundUIOpenedEvent>(OnConsoleUIOpened);
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsolePurchaseMessage>(OnPurchaseMessage);
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleSellMessage>(OnSellMessage);
        SubscribeLocalEvent<ShipyardConsoleComponent, EntInsertedIntoContainerMessage>(OnItemSlotChanged);
        SubscribeLocalEvent<ShipyardConsoleComponent, EntRemovedFromContainerMessage>(OnItemSlotChanged);
        SubscribeLocalEvent<StationDeedSpawnerComponent, MapInitEvent>(OnInitDeedSpawner);
    }

    private void OnPurchaseMessage(EntityUid uid, ShipyardConsoleComponent component, ShipyardConsolePurchaseMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId
            || !TryComp<IdCardComponent>(targetId, out var idCard))
        {
            Deny(uid, component, player, "shipyard-console-no-idcard");
            return;
        }

        if (HasComp<ShuttleDeedComponent>(targetId))
        {
            Deny(uid, component, player, "shipyard-console-already-deeded");
            return;
        }

        if (!HasConsoleAccess(uid, player, targetId))
        {
            Deny(uid, component, player, "comms-console-permission-denied");
            return;
        }

        if (!_proto.TryIndex<VesselPrototype>(args.Vessel, out var vessel))
        {
            Deny(uid, component, player, "shipyard-console-invalid-vessel");
            return;
        }

        if (!GetAvailableShuttles(uid, component).Contains(vessel.ID))
        {
            PlayDenySound(uid, component);
            _adminLogger.Add(LogType.Shipyard, LogImpact.Medium,
                $"{ToPrettyString(player):player} tried to buy {vessel.ID}, which {ToPrettyString(uid):console} does not offer");
            return;
        }

        if (vessel.Price <= 0)
        {
            Deny(uid, component, player, "shipyard-console-invalid-price");
            return;
        }

        if (_station.GetOwningStation(uid) is not { Valid: true } station)
        {
            Deny(uid, component, player, "shipyard-console-invalid-station");
            return;
        }

        var billBalance = GetBillBalance(uid, component);
        if (billBalance <= 0)
        {
            Deny(uid, component, player, "shipyard-console-no-bills");
            return;
        }

        if (billBalance < vessel.Price)
        {
            Deny(uid, component, player, "cargo-console-insufficient-funds", ("cost", vessel.Price));
            return;
        }

        // Resolve the buyer before spending anything: the deed needs their user id, and failing after the ship
        // has been spawned and paid for would leave them without a deed.
        if (!_players.TryGetSessionByEntity(player, out var playerSession))
        {
            Deny(uid, component, player, "shipyard-console-purchase-failed");
            return;
        }

        if (!TryPurchaseShuttle(station, vessel.ShuttlePath, vessel.Delay, out var shuttle))
        {
            Deny(uid, component, player, "shipyard-console-purchase-failed");
            return;
        }

        // Consume F-Bills from the slot; change stays in the slot if overpaid.
        var billEnt = component.BillSlot.ContainerSlot!.ContainedEntity!.Value;
        // Serenity: paid with a character's starting funds? Remember whose, so selling can't launder them.
        (int ProfileId, string OwnerName)? boundBy = TryComp<BoundCashComponent>(billEnt, out var boundCash)
            ? (boundCash.ProfileId, boundCash.OwnerName)
            : null;
        _stackSystem.SetCount(billEnt, billBalance - vessel.Price);
        // (SetCount to 0 auto-deletes the stack entity.)

        var name = vessel.Name;

        // A vessel with a matching game map prototype becomes its own station so late-joiners can spawn aboard.
        EntityUid? shuttleStation = null;
        if (_proto.TryIndex<GameMapPrototype>(vessel.ID, out var stationProto) && stationProto.Stations.TryGetValue(vessel.ID, out var stationConfig))
        {
            shuttleStation = _station.InitializeNewStation(stationConfig, new[] { shuttle.Value });
            name = MetaData(shuttleStation.Value).EntityName;
            _shuttle.SetIFFColor(shuttle.Value, new Color(10, 50, 100, 100));
        }

        if (TryComp<AccessComponent>(targetId, out var access) && component.NewAccessLevels.Count > 0)
        {
            var tags = access.Tags.ToList();
            tags.AddRange(component.NewAccessLevels);
            _access.TrySetTags(targetId, tags, access);
        }

        var userId = playerSession.UserId;
        AssignDeed(EnsureComp<ShuttleDeedComponent>(targetId), shuttle.Value, name, player, userId);
        AssignDeed(EnsureComp<ShuttleDeedComponent>(shuttle.Value), shuttle.Value, name, player, userId);
        if (boundBy is { } bound) // Serenity
        {
            foreach (var deedEnt in new[] { targetId, shuttle.Value })
            {
                var deed = Comp<ShuttleDeedComponent>(deedEnt);
                deed.BoundPaid = vessel.Price;
                deed.BoundProfileId = bound.ProfileId;
                deed.BoundOwnerName = bound.OwnerName;
            }
        }
        Dirty(targetId, Comp<ShuttleDeedComponent>(targetId));
        Dirty(shuttle.Value, Comp<ShuttleDeedComponent>(shuttle.Value));

        if (!string.IsNullOrEmpty(component.NewJobTitle))
            _idCard.TryChangeJobTitle(targetId, component.NewJobTitle, idCard, player);

        if (shuttleStation != null)
            CopyRecordToStation(targetId, shuttleStation.Value);

        var deedName = GetFullName(Comp<ShuttleDeedComponent>(targetId));
        Announce(uid, component.ShipyardChannel, Loc.GetString("shipyard-console-docking",
            ("owner", Identity(player)), ("vessel", deedName), ("delay", (int) vessel.Delay)));
        if (component.SecretShipyardChannel is { } secret)
            Announce(uid, secret, Loc.GetString("shipyard-console-docking-secret"));

        PlayConfirmSound(uid, component);
        _adminLogger.Add(LogType.Shipyard, LogImpact.Medium,
            $"{ToPrettyString(player):player} bought {vessel.ID} ({ToPrettyString(shuttle.Value):ship}) for {vessel.Price} Federal Bills at {ToPrettyString(uid):console}");
        _adminLogger.Add(LogType.Economy, LogImpact.Low,
            $"{ToPrettyString(player):player} paid {vessel.Price} Federal Bills for ship {vessel.ID} (change {billBalance - vessel.Price})");

        RefreshState(uid, component, player);
    }

    private void OnSellMessage(EntityUid uid, ShipyardConsoleComponent component, ShipyardConsoleSellMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId
            || !HasComp<IdCardComponent>(targetId))
        {
            Deny(uid, component, player, "shipyard-console-no-idcard");
            return;
        }

        if (!TryComp<ShuttleDeedComponent>(targetId, out var deed) || deed.ShuttleUid is not { Valid: true } shuttleUid || !Exists(shuttleUid))
        {
            RemComp<ShuttleDeedComponent>(targetId);
            Deny(uid, component, player, "shipyard-console-no-deed");
            return;
        }

        if (_station.GetOwningStation(uid) is not { Valid: true } station)
        {
            Deny(uid, component, player, "shipyard-console-invalid-station");
            return;
        }

        // Move the seller's crew record back to the station before the ship's station is deleted.
        if (_station.GetOwningStation(shuttleUid) is { Valid: true } shuttleStation && shuttleStation != station)
            CopyRecordToStation(targetId, station, onlyFrom: shuttleStation);

        var shipName = GetFullName(deed);
        var shipPretty = ToPrettyString(shuttleUid);

        if (!TrySellShuttle(station, shuttleUid, out var bill))
        {
            Deny(uid, component, player, "shipyard-console-sale-reqs");
            return;
        }

        RemComp<ShuttleDeedComponent>(targetId);

        var tax = CalculateSalesTax(component, bill);
        if (tax > 0 && TryComp<StationBankAccountComponent>(station, out var bank))
        {
            _cargo.UpdateBankAccount((station, bank), tax, bank.PrimaryAccount);
            bill -= tax;
        }

        // Serenity: whatever was paid in a character's starting funds comes back bound to that character.
        var boundBack = deed.BoundProfileId != null ? Math.Min(bill, deed.BoundPaid) : 0;
        if (boundBack > 0)
        {
            var boundEnt = Spawn("SpaceCashBound", Transform(uid).Coordinates);
            if (TryComp<StackComponent>(boundEnt, out var boundStack))
                _stackSystem.SetCount(boundEnt, boundBack, boundStack);
            _boundCash.Bind(boundEnt, deed.BoundProfileId!.Value, deed.ShuttleOwner, deed.BoundOwnerName);
        }

        // Spawn sale proceeds as Federal Bills at the console.
        if (bill - boundBack > 0)
        {
            var cashEnt = Spawn("SpaceCash", Transform(uid).Coordinates);
            if (TryComp<StackComponent>(cashEnt, out var cashStack))
                _stackSystem.SetCount(cashEnt, bill - boundBack, cashStack);
        }

        PlayConfirmSound(uid, component);

        Announce(uid, component.ShipyardChannel, Loc.GetString("shipyard-console-leaving",
            ("owner", Identity(deed.ShuttleOwner ?? player)), ("vessel", shipName), ("player", Identity(player))));
        if (component.SecretShipyardChannel is { } secret)
            Announce(uid, secret, Loc.GetString("shipyard-console-leaving-secret"));

        _adminLogger.Add(LogType.Shipyard, LogImpact.Medium,
            $"{ToPrettyString(player):player} sold {shipPretty} for {bill} Federal Bills (tax {tax}) at {ToPrettyString(uid):console}");
        _adminLogger.Add(LogType.Economy, LogImpact.Low,
            $"{ToPrettyString(player):player} received {bill} Federal Bills for selling ship {shipName}");

        RefreshState(uid, component, player);
    }

    private void OnConsoleUIOpened(EntityUid uid, ShipyardConsoleComponent component, BoundUIOpenedEvent args)
    {
        if (args.Actor is { Valid: true } player)
            RefreshState(uid, component, player);
    }

    private void OnItemSlotChanged(EntityUid uid, ShipyardConsoleComponent component, ContainerModifiedMessage args)
    {
        if (args.Container.ID != ShipyardConsoleComponent.TargetIdCardSlotId
            && args.Container.ID != ShipyardConsoleComponent.BillSlotId)
            return;

        foreach (var actor in _ui.GetActors(uid, ShipyardConsoleUiKey.Shipyard))
            RefreshState(uid, component, actor);
    }

    private void RefreshState(EntityUid uid, ShipyardConsoleComponent component, EntityUid player)
    {
        var targetId = component.TargetIdSlot.ContainerSlot?.ContainedEntity;

        string? deedTitle = null;
        var sellValue = 0;
        if (targetId != null && TryComp<ShuttleDeedComponent>(targetId.Value, out var deed))
        {
            if (deed.ShuttleUid is not { } ship || !Exists(ship))
            {
                RemComp<ShuttleDeedComponent>(targetId.Value);
            }
            else
            {
                deedTitle = GetFullName(deed);
                sellValue = (int) _pricing.AppraiseGrid(ship);
                sellValue -= CalculateSalesTax(component, sellValue);
            }
        }

        var balance = GetBillBalance(uid, component);
        var access = HasConsoleAccess(uid, player, targetId);

        var state = new ShipyardConsoleInterfaceState(
            (int) balance,
            access,
            deedTitle,
            sellValue,
            targetId != null,
            GetAvailableShuttles(uid, component));

        _ui.SetUiState(uid, ShipyardConsoleUiKey.Shipyard, state);
    }

    /// <summary>
    /// Every vessel prototype ID this console sells: group matches plus explicit listings.
    /// </summary>
    public List<string> GetAvailableShuttles(EntityUid uid, ShipyardConsoleComponent? component = null, ShipyardListingComponent? listing = null)
    {
        var result = new List<string>();

        if (Resolve(uid, ref component, false))
        {
            foreach (var vessel in _proto.EnumeratePrototypes<VesselPrototype>())
            {
                if (component.Groups.Contains(vessel.Group))
                    result.Add(vessel.ID);
            }
        }

        if (Resolve(uid, ref listing, false))
        {
            foreach (var id in listing.Shuttles)
            {
                if (!result.Contains(id))
                    result.Add(id);
            }
        }

        return result;
    }

    /// <summary>
    /// Whether the console's access reader lets this purchase through. The ID card in the console's slot counts
    /// as well as whatever the player is carrying, so inserting a card with the right access is enough.
    /// </summary>
    private bool HasConsoleAccess(EntityUid uid, EntityUid player, EntityUid? targetId)
    {
        if (!TryComp<AccessReaderComponent>(uid, out var reader) || !reader.Enabled)
            return true;

        return targetId is { Valid: true } card && _accessReader.IsAllowed(card, uid, reader)
            || _accessReader.IsAllowed(player, uid, reader);
    }

    /// <summary>
    /// Returns the total Federal Bill count in the console's bill slot.
    /// </summary>
    private int GetBillBalance(EntityUid _, ShipyardConsoleComponent component)
    {
        var entity = component.BillSlot.ContainerSlot?.ContainedEntity;
        if (entity is not { Valid: true } billEnt)
            return 0;
        if (!TryComp<StackComponent>(billEnt, out var stack))
            return 0;
        return stack.Count;
    }

    private void CopyRecordToStation(EntityUid idCard, EntityUid station, EntityUid? onlyFrom = null)
    {
        if (!TryComp<StationRecordKeyStorageComponent>(idCard, out var keyStorage) || keyStorage.Key is not { } key)
            return;

        if (onlyFrom != null && key.OriginStation != onlyFrom)
            return;

        if (!_records.TryGetRecord<GeneralStationRecord>(key, out var record))
            return;

        _records.AddRecordEntry(station, record);
        _records.Synchronize(station);
    }

    private static void AssignDeed(ShuttleDeedComponent deed, EntityUid shuttle, string name, EntityUid owner, Robust.Shared.Network.NetUserId userId)
    {
        deed.ShuttleUid = shuttle;
        deed.ShuttleOwner = owner;
        deed.OwnerUserId = userId;
        ParseShuttleName(deed, name);
    }

    /// <summary>
    /// Splits "Name NNN-XXXX" into name and suffix. A trailing short token containing a dash is the suffix.
    /// </summary>
    private static void ParseShuttleName(ShuttleDeedComponent deed, string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hasSuffix = parts.Length > 1
            && parts[^1].Length < ShuttleDeedComponent.MaxSuffixLength
            && parts[^1].Contains('-');

        deed.ShuttleNameSuffix = hasSuffix ? parts[^1] : null;
        deed.ShuttleName = string.Join(' ', hasSuffix ? parts[..^1] : parts);
    }

    private static int CalculateSalesTax(ShipyardConsoleComponent component, int sellValue)
    {
        if (!float.IsFinite(component.SalesTax) || component.SalesTax <= 0f)
            return 0;

        return (int) (sellValue * component.SalesTax);
    }

    private void OnInitDeedSpawner(EntityUid uid, StationDeedSpawnerComponent component, MapInitEvent args)
    {
        if (!HasComp<IdCardComponent>(uid))
            return;

        var xform = Transform(uid);
        if (xform.GridUid is not { } grid
            || !TryComp<ShuttleDeedComponent>(grid, out var shipDeed)
            || !HasComp<ShuttleComponent>(grid))
            return;

        if (shipDeed.ShuttleOwner is { } owner)
        {
            var ownerName = ParentheticalRegex.Replace(ToPrettyString(owner).ToString(), "");
            _idCard.TryChangeFullName(uid, ownerName);
        }

        var deed = EnsureComp<ShuttleDeedComponent>(uid);
        deed.ShuttleUid = shipDeed.ShuttleUid;
        deed.ShuttleName = shipDeed.ShuttleName;
        deed.ShuttleNameSuffix = shipDeed.ShuttleNameSuffix;
        deed.ShuttleOwner = shipDeed.ShuttleOwner;
        deed.OwnerUserId = shipDeed.OwnerUserId;
        Dirty(uid, deed);
    }

    private void Announce(EntityUid console, ProtoId<Content.Shared.Radio.RadioChannelPrototype> channel, string text)
    {
        _radio.SendRadioMessage(console, text, channel, console);
        _chat.TrySendInGameICMessage(console, text, InGameICChatType.Speak, true);
    }

    private string Identity(EntityUid uid)
    {
        return Content.Shared.IdentityManagement.Identity.Name(uid, EntityManager);
    }

    private void Deny(EntityUid uid, ShipyardConsoleComponent component, EntityUid player, string locKey, params (string, object)[] args)
    {
        _popup.PopupEntity(Loc.GetString(locKey, args), uid, player);
        PlayDenySound(uid, component);
    }

    private void PlayDenySound(EntityUid uid, ShipyardConsoleComponent component)
    {
        _audio.PlayPvs(component.ErrorSound, uid, AudioParams.Default.WithMaxDistance(5f));
    }

    private void PlayConfirmSound(EntityUid uid, ShipyardConsoleComponent component)
    {
        _audio.PlayPvs(component.ConfirmSound, uid, AudioParams.Default.WithMaxDistance(5f));
    }
}
