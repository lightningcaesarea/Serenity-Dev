// Ship saving, after Hardlight's ShipyardGridSaveSystem (fenndragon/HardLight, MIT licensed), reworked for
// Serenity: saves stay on the server instead of being handed to the client, so a player can only ever load back a
// ship the server wrote for their own character.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using Content.Server._Serenity.Economy;
using Content.Shared._Serenity.CCVar;
using Content.Shared._Serenity.Shipyard.BUI;
using Content.Shared._Serenity.Shipyard.Components;
using Content.Shared._Serenity.Shipyard.Events;
using Content.Shared.Access.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Database;
using Content.Shared.DeviceLinking;
using Content.Shared.Mind.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Stacks;
using Content.Shared.Station.Components;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Serenity.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    private static readonly ResPath SaveRoot = new("/ShipSaves");

    /// <summary>Seconds before a loaded ship FTL-docks to the station, as with a purchase.</summary>
    private const float SavedShipDelay = 10f;

    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private ICharacterBalanceManager _characters = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private Content.Server.DeviceLinking.Systems.DeviceLinkSystem _deviceLink = default!;

    /// <summary>When each player last loaded a saved ship, for the load cooldown.</summary>
    private readonly Dictionary<NetUserId, TimeSpan> _lastLoad = new();

    /// <summary>What is written next to each saved ship: enough to list it without reading the ship itself.</summary>
    private sealed record SavedShipInfo(string Id, string Name, int Value, DateTime SavedAt);

    private void InitializeSaves()
    {
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleSaveMessage>(OnSaveMessage);
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleLoadMessage>(OnLoadMessage);
    }

    private void OnSaveMessage(EntityUid uid, ShipyardConsoleComponent component, ShipyardConsoleSaveMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!_cfg.GetCVar(SerenityCCVars.ShipSavesEnabled))
        {
            Deny(uid, component, player, "shipyard-console-saves-disabled");
            return;
        }

        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId
            || !HasComp<IdCardComponent>(targetId))
        {
            Deny(uid, component, player, "shipyard-console-no-idcard");
            return;
        }

        if (!TryComp<ShuttleDeedComponent>(targetId, out var deed) || deed.ShuttleUid is not { Valid: true } shuttleUid || !Exists(shuttleUid))
        {
            Deny(uid, component, player, "shipyard-console-no-deed");
            return;
        }

        if (!_players.TryGetSessionByEntity(player, out var session)
            || deed.OwnerUserId != session.UserId
            || _characters.GetActiveProfileId(session.UserId) is not { } profile)
        {
            Deny(uid, component, player, "shipyard-console-save-not-owner");
            return;
        }

        if (ListSaves(session.UserId, profile).Count >= _cfg.GetCVar(SerenityCCVars.ShipSavesPerCharacter))
        {
            Deny(uid, component, player, "shipyard-console-save-full",
                ("max", _cfg.GetCVar(SerenityCCVars.ShipSavesPerCharacter)));
            return;
        }

        if (_station.GetOwningStation(uid) is not { Valid: true } station)
        {
            Deny(uid, component, player, "shipyard-console-invalid-station");
            return;
        }

        // As with a sale: move the owner's crew record back to the station before the ship's station goes.
        if (_station.GetOwningStation(shuttleUid) is { Valid: true } shuttleStation && shuttleStation != station)
            CopyRecordToStation(targetId, station, onlyFrom: shuttleStation);

        var shipName = GetFullName(deed);
        var shipPretty = ToPrettyString(shuttleUid);

        if (!TrySaveShuttle(station, shuttleUid, session.UserId, profile, shipName, out var info))
        {
            Deny(uid, component, player, "shipyard-console-save-reqs");
            return;
        }

        RemComp<ShuttleDeedComponent>(targetId);

        PlayConfirmSound(uid, component);
        Announce(uid, component.ShipyardChannel, Loc.GetString("shipyard-console-saved",
            ("owner", Identity(player)), ("vessel", shipName)));

        _adminLogger.Add(LogType.Shipyard, LogImpact.Medium,
            $"{ToPrettyString(player):player} saved {shipPretty} as '{info.Name}' (save {info.Id}, value {info.Value}) at {ToPrettyString(uid):console}");

        RefreshState(uid, component, player);
    }

    private void OnLoadMessage(EntityUid uid, ShipyardConsoleComponent component, ShipyardConsoleLoadMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!_cfg.GetCVar(SerenityCCVars.ShipSavesEnabled))
        {
            Deny(uid, component, player, "shipyard-console-saves-disabled");
            return;
        }

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

        if (!_players.TryGetSessionByEntity(player, out var session)
            || _characters.GetActiveProfileId(session.UserId) is not { } profile)
        {
            Deny(uid, component, player, "shipyard-console-purchase-failed");
            return;
        }

        // The id only ever picks one of this character's own saves; anything else simply isn't found.
        var info = ListSaves(session.UserId, profile).FirstOrDefault(s => s.Id == args.SaveId);
        if (info == null)
        {
            Deny(uid, component, player, "shipyard-console-load-missing");
            return;
        }

        var now = _timing.CurTime;
        var cooldown = TimeSpan.FromSeconds(_cfg.GetCVar(SerenityCCVars.ShipSaveLoadCooldown));
        if (_lastLoad.TryGetValue(session.UserId, out var last) && now < last + cooldown)
        {
            Deny(uid, component, player, "shipyard-console-load-cooldown",
                ("seconds", (int) Math.Ceiling((last + cooldown - now).TotalSeconds)));
            return;
        }

        if (_station.GetOwningStation(uid) is not { Valid: true } station)
        {
            Deny(uid, component, player, "shipyard-console-invalid-station");
            return;
        }

        var fee = LoadFee(info);
        var billBalance = GetBillBalance(uid, component);
        if (billBalance < fee)
        {
            Deny(uid, component, player, "cargo-console-insufficient-funds", ("cost", fee));
            return;
        }

        if (!TryLoadSavedShuttle(station, session.UserId, profile, info, out var shuttle))
        {
            Deny(uid, component, player, "shipyard-console-purchase-failed");
            return;
        }

        _lastLoad[session.UserId] = now;

        if (fee > 0)
        {
            var billEnt = component.BillSlot.ContainerSlot!.ContainedEntity!.Value;
            _stackSystem.SetCount(billEnt, billBalance - fee);
        }

        if (TryComp<AccessComponent>(targetId, out var access) && component.NewAccessLevels.Count > 0)
        {
            var tags = access.Tags.ToList();
            tags.AddRange(component.NewAccessLevels);
            _access.TrySetTags(targetId, tags, access);
        }

        foreach (var deedEnt in new[] { targetId, shuttle.Value })
        {
            var deed = EnsureComp<ShuttleDeedComponent>(deedEnt);
            AssignDeed(deed, shuttle.Value, info.Name, player, session.UserId);
            deed.LoadedFromSave = true;
            Dirty(deedEnt, deed);
        }

        _metaData.SetEntityName(shuttle.Value, info.Name);

        if (!string.IsNullOrEmpty(component.NewJobTitle))
            _idCard.TryChangeJobTitle(targetId, component.NewJobTitle, idCard, player);

        // Loading uses the save up: the ship is back in the sector, and the next save writes a fresh one.
        DeleteSave(session.UserId, profile, info.Id);

        Announce(uid, component.ShipyardChannel, Loc.GetString("shipyard-console-docking",
            ("owner", Identity(player)), ("vessel", info.Name), ("delay", (int) SavedShipDelay)));

        PlayConfirmSound(uid, component);
        _adminLogger.Add(LogType.Shipyard, LogImpact.Medium,
            $"{ToPrettyString(player):player} loaded saved ship '{info.Name}' (save {info.Id}) as {ToPrettyString(shuttle.Value):ship} for {fee} Federal Bills at {ToPrettyString(uid):console}");

        RefreshState(uid, component, player);
    }

    /// <summary>
    /// Whether the console should offer to save the deeded ship: saves are on and the player owns it.
    /// The save handler re-checks all of this.
    /// </summary>
    private bool CanSaveShip(EntityUid player, ShuttleDeedComponent deed)
    {
        return _cfg.GetCVar(SerenityCCVars.ShipSavesEnabled)
            && _players.TryGetSessionByEntity(player, out var session)
            && deed.OwnerUserId == session.UserId;
    }

    /// <summary>
    /// The saved ships of the character <paramref name="player"/> is playing, as the console lists them.
    /// </summary>
    private List<SavedShipEntry> GetSavedShipEntries(EntityUid player)
    {
        if (!_cfg.GetCVar(SerenityCCVars.ShipSavesEnabled)
            || !_players.TryGetSessionByEntity(player, out var session)
            || _characters.GetActiveProfileId(session.UserId) is not { } profile)
            return new List<SavedShipEntry>();

        return ListSaves(session.UserId, profile)
            .Select(s => new SavedShipEntry(s.Id, s.Name, LoadFee(s)))
            .ToList();
    }

    private int LoadFee(SavedShipInfo info)
    {
        var fraction = _cfg.GetCVar(SerenityCCVars.ShipSaveLoadFee);
        if (!float.IsFinite(fraction) || fraction <= 0f)
            return 0;

        return (int) (info.Value * fraction);
    }

    /// <summary>
    /// Saves a ship docked to the station for its owner's character and removes it from the round.
    /// Loose items, cargo and anything inside machines are left behind, as on Hardlight: what persists is the
    /// ship itself.
    /// </summary>
    private bool TrySaveShuttle(EntityUid stationUid, EntityUid shuttleUid, NetUserId user, int profile, string name,
        [NotNullWhen(true)] out SavedShipInfo? info)
    {
        info = null;

        if (!TryComp<StationDataComponent>(stationUid, out var stationData) || !HasComp<ShuttleComponent>(shuttleUid))
            return false;

        if (_station.GetLargestGrid((stationUid, stationData)) is not { } targetGrid || !IsDockedTo(shuttleUid, targetGrid))
        {
            Log.Info($"Shipyard: refusing to save {ToPrettyString(shuttleUid)}, not docked to the station.");
            return false;
        }

        if (HasMindAboard(shuttleUid))
        {
            Log.Info($"Shipyard: refusing to save {ToPrettyString(shuttleUid)}, someone is aboard.");
            return false;
        }

        _docking.UndockDocks(shuttleUid);
        PurgeTransientEntities(shuttleUid);
        CleanupBrokenDeviceLinks(shuttleUid);

        // The deed points at the owner's body, which isn't part of the ship. Loading adds a fresh one.
        RemComp<ShuttleDeedComponent>(shuttleUid);

        var value = (int) _pricing.AppraiseGrid(shuttleUid);

        var options = SerializationOptions.Default with
        {
            MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
            ErrorOnOrphan = false,
            LogAutoInclude = null,
        };

        var writer = new StringWriter();
        if (!_loader.TrySaveGrid(shuttleUid, writer, options))
        {
            Log.Error($"Shipyard: failed to serialize {ToPrettyString(shuttleUid)} for saving.");
            return false;
        }

        var id = Guid.NewGuid().ToString("N");
        info = new SavedShipInfo(id, name, value, DateTime.UtcNow);
        var dir = SaveDir(user, profile);

        try
        {
            _resources.UserData.CreateDir(dir);
            _resources.UserData.WriteAllText(dir / $"{id}.yml", writer.ToString());
            _resources.UserData.WriteAllText(dir / $"{id}.json", JsonSerializer.Serialize(info));
        }
        catch (Exception e)
        {
            Log.Error($"Shipyard: failed to write ship save {id}: {e}");
            info = null;
            return false;
        }

        if (_station.GetOwningStation(shuttleUid) is { Valid: true } shuttleStation && shuttleStation != stationUid)
            _station.DeleteStation(shuttleStation);

        Del(shuttleUid);
        Log.Info($"Shipyard: saved {name} as {id} for {user} (character {profile}), value {value}");
        return true;
    }

    private bool TryLoadSavedShuttle(EntityUid stationUid, NetUserId user, int profile, SavedShipInfo info,
        [NotNullWhen(true)] out EntityUid? shuttleUid)
    {
        shuttleUid = null;
        var path = SaveDir(user, profile) / $"{info.Id}.yml";

        if (!_resources.UserData.Exists(path))
        {
            Log.Error($"Shipyard: ship save {info.Id} has no grid file.");
            return false;
        }

        using var reader = _resources.UserData.OpenText(path);
        return TryDeliverShuttle(stationUid, reader, path.ToString(), SavedShipDelay, out shuttleUid);
    }

    private List<SavedShipInfo> ListSaves(NetUserId user, int profile)
    {
        var dir = SaveDir(user, profile);
        var result = new List<SavedShipInfo>();

        if (!_resources.UserData.IsDir(dir))
            return result;

        foreach (var entry in _resources.UserData.DirectoryEntries(dir))
        {
            if (!entry.EndsWith(".json"))
                continue;

            try
            {
                var info = JsonSerializer.Deserialize<SavedShipInfo>(_resources.UserData.ReadAllText(dir / entry));
                if (info != null && _resources.UserData.Exists(dir / $"{info.Id}.yml"))
                    result.Add(info);
            }
            catch (Exception e)
            {
                Log.Warning($"Shipyard: unreadable ship save {dir / entry}: {e.Message}");
            }
        }

        result.Sort((a, b) => a.SavedAt.CompareTo(b.SavedAt));
        return result;
    }

    private void DeleteSave(NetUserId user, int profile, string id)
    {
        var dir = SaveDir(user, profile);
        foreach (var file in new[] { dir / $"{id}.yml", dir / $"{id}.json" })
        {
            if (_resources.UserData.Exists(file))
                _resources.UserData.Delete(file);
        }
    }

    private static ResPath SaveDir(NetUserId user, int profile)
    {
        return SaveRoot / user.UserId.ToString() / profile.ToString();
    }

    /// <summary>
    /// True when anything with a mind is aboard, alive or not: saving would take them out of the round.
    /// </summary>
    private bool HasMindAboard(EntityUid shuttleUid)
    {
        var query = EntityQueryEnumerator<MindContainerComponent, TransformComponent>();
        while (query.MoveNext(out _, out var mind, out var xform))
        {
            if (xform.GridUid == shuttleUid && mind.HasMind)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Deletes everything on the ship that isn't part of it: anything unanchored, whether loose or inside a
    /// container. Anchored fixtures, wall mounts, static bodies and solutions stay. Machines get fresh boards and
    /// parts when the ship is loaded again.
    /// </summary>
    private void PurgeTransientEntities(EntityUid shuttleUid)
    {
        var toDelete = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var ent, out var xform))
        {
            if (ent == shuttleUid || xform.GridUid != shuttleUid)
                continue;

            if (IsTransient(ent, xform))
                toDelete.Add(ent);

            if (TryComp<ContainerManagerComponent>(ent, out var manager))
                CollectContained(manager, toDelete);
        }

        foreach (var ent in toDelete)
        {
            if (!TerminatingOrDeleted(ent))
                Del(ent);
        }
    }

    private void CollectContained(ContainerManagerComponent manager, HashSet<EntityUid> toDelete)
    {
        foreach (var container in manager.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
            {
                if (IsTransient(contained, Transform(contained)))
                    toDelete.Add(contained);

                if (TryComp<ContainerManagerComponent>(contained, out var inner))
                    CollectContained(inner, toDelete);
            }
        }
    }

    private bool IsTransient(EntityUid ent, TransformComponent xform)
    {
        if (xform.Anchored)
            return false;

        if (HasComp<Robust.Shared.Map.Components.MapGridComponent>(ent)
            || HasComp<Content.Shared.Wall.WallMountComponent>(ent)
            || HasComp<SolutionComponent>(ent)
            || HasComp<ContainedSolutionComponent>(ent))
            return false;

        return !TryComp<PhysicsComponent>(ent, out var physics) || physics.BodyType != BodyType.Static;
    }

    /// <summary>
    /// Drops device links to anything the purge deleted, which would otherwise fail to serialize.
    /// </summary>
    private void CleanupBrokenDeviceLinks(EntityUid shuttleUid)
    {
        var query = EntityQueryEnumerator<DeviceLinkSourceComponent, TransformComponent>();
        while (query.MoveNext(out var source, out var comp, out var xform))
        {
            if (xform.GridUid != shuttleUid)
                continue;

            foreach (var sink in comp.LinkedPorts.Keys.ToList())
            {
                if (TerminatingOrDeleted(sink))
                    _deviceLink.RemoveSinkFromSource(source, sink, comp);
            }
        }
    }
}
