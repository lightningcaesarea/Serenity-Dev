// Ported from Frontier Station 14 at commit 5be37d18c2 (2024-07-01), MIT licensed.
// Copyright (c) 2017-2024 New Frontiers. Adapted for Serenity: modern MapLoader/MapSystem APIs,
// delayed FTL-dock arrival kept from Starlight's version, player-credit economy.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics;
using Content.Server.Cargo.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._Serenity.Shipyard;
using Content.Shared._Serenity.Shipyard.Components;
using Content.Shared._Starlight.CCVar;
using Content.Shared.Shuttles.Components;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Serenity.Shipyard.Systems;

public sealed partial class ShipyardSystem : SharedShipyardSystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private MapSystem _mapSystem = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private MetaDataSystem _metaData = default!;

    public EntityUid? ShipyardMapEntity { get; private set; }
    public MapId? ShipyardMapId { get; private set; }

    private float _shuttleIndex;
    private const float ShuttleSpawnBuffer = 1f;
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        _enabled = _cfg.GetCVar(StarlightCCVars.Shipyard);
        _cfg.OnValueChanged(StarlightCCVars.Shipyard, SetShipyardEnabled);

        SubscribeLocalEvent<ShipyardConsoleComponent, ComponentStartup>(OnShipyardStartup);
        SubscribeLocalEvent<ShipyardConsoleComponent, ComponentShutdown>(OnShipyardShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        InitializeConsole();
        InitializeSaves(); // Serenity: ship saving
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _cfg.UnsubValueChanged(StarlightCCVars.Shipyard, SetShipyardEnabled);
        CleanupShipyard();
    }

    private void OnShipyardStartup(EntityUid uid, ShipyardConsoleComponent component, ComponentStartup args)
    {
        if (_enabled)
            SetupShipyard();
    }

    private void OnShipyardShutdown(EntityUid uid, ShipyardConsoleComponent component, ComponentShutdown args)
    {
        var query = EntityQueryEnumerator<ShipyardConsoleComponent>();
        while (query.MoveNext(out var otherUid, out _))
        {
            if (otherUid != uid)
                return;
        }

        CleanupShipyard();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        CleanupShipyard();
    }

    private void SetShipyardEnabled(bool value)
    {
        if (_enabled == value)
            return;

        _enabled = value;

        if (value)
            SetupShipyard();
        else
            CleanupShipyard();
    }

    /// <summary>
    /// Loads a ship onto the shipyard map and, after <paramref name="delay"/> seconds, FTL-docks it to the
    /// station's largest grid.
    /// </summary>
    public bool TryPurchaseShuttle(EntityUid stationUid, ResPath shuttlePath, float delay, [NotNullWhen(true)] out EntityUid? shuttleUid)
    {
        return TryDeliverShuttle(stationUid, shuttlePath.ToString(), delay, out shuttleUid,
            (map, offset) => _loader.TryLoadGrid(map, shuttlePath, out var grid, offset: offset) ? grid : null);
    }

    /// <summary>
    /// Serenity: same as <see cref="TryPurchaseShuttle"/>, but loads the grid from serialized YAML
    /// (a saved ship) instead of a resource path.
    /// </summary>
    public bool TryDeliverShuttle(EntityUid stationUid, TextReader reader, string source, float delay, [NotNullWhen(true)] out EntityUid? shuttleUid)
    {
        return TryDeliverShuttle(stationUid, source, delay, out shuttleUid,
            (map, offset) => _loader.TryLoadGrid(map, reader, source, out var grid, offset: offset) ? grid : null);
    }

    private bool TryDeliverShuttle(
        EntityUid stationUid,
        string source,
        float delay,
        [NotNullWhen(true)] out EntityUid? shuttleUid,
        Func<MapId, Vector2, Entity<MapGridComponent>?> load)
    {
        shuttleUid = null;

        if (!_enabled)
        {
            Log.Warning($"Shipyard delivery of {source} refused: shipyard is disabled.");
            return false;
        }

        if (!TryComp<StationDataComponent>(stationUid, out var stationData))
        {
            Log.Warning($"Shipyard delivery of {source} refused: {ToPrettyString(stationUid)} is not a station.");
            return false;
        }

        var targetGrid = _station.GetLargestGrid((stationUid, stationData));
        if (targetGrid == null)
        {
            Log.Warning($"Shipyard delivery of {source} refused: station has no grid to dock to.");
            return false;
        }

        if (!TryAddShuttle(source, load, out var grid))
            return false;

        if (!HasComp<ShuttleComponent>(grid.Value))
        {
            Log.Error($"Shipyard: {source} has no ShuttleComponent, deleting it.");
            RemoveFromShipyard(grid.Value);
            return false;
        }

        var price = _pricing.AppraiseGrid(grid.Value);
        Log.Info($"Shipyard: {source} delivered to {ToPrettyString(stationUid)}, appraised at {price:f0}");

        var shuttle = grid.Value;
        var target = targetGrid.Value;
        var checkedDelay = float.IsFinite(delay) && delay >= 1f ? delay : 1f;

        Timer.Spawn(TimeSpan.FromSeconds(checkedDelay), () =>
        {
            if (Deleted(shuttle) || Deleted(target))
                return;

            if (!TryComp<ShuttleComponent>(shuttle, out var shuttleComp))
                return;

            _shuttle.TryFTLDock(shuttle, shuttleComp, target);
        });

        shuttleUid = shuttle;
        return true;
    }

    private bool TryAddShuttle(
        string source,
        Func<MapId, Vector2, Entity<MapGridComponent>?> load,
        [NotNullWhen(true)] out EntityUid? shuttleGrid)
    {
        shuttleGrid = null;

        if (ShipyardMapId == null || ShipyardMapEntity == null || !Exists(ShipyardMapEntity.Value))
            SetupShipyard();

        if (ShipyardMapId == null)
        {
            Log.Error($"Shipyard: no shipyard map, cannot load {source}.");
            return false;
        }

        var offset = new Vector2(500f + _shuttleIndex, 1f);
        if (load(ShipyardMapId.Value, offset) is not { } grid)
        {
            Log.Error($"Shipyard: failed to load {source}.");
            return false;
        }

        _shuttleIndex += grid.Comp.LocalAABB.Width + ShuttleSpawnBuffer;
        shuttleGrid = grid.Owner;
        return true;
    }

    private void RemoveFromShipyard(EntityUid grid)
    {
        if (TryComp<MapGridComponent>(grid, out var gridComp))
            _shuttleIndex = MathF.Max(0f, _shuttleIndex - gridComp.LocalAABB.Width - ShuttleSpawnBuffer);

        if (Exists(grid))
            Del(grid);
    }

    /// <summary>
    /// Sells a ship: it must be docked to the station and carry nobody alive. Deletes the ship and any
    /// station attached to it, and returns its appraised value.
    /// </summary>
    public bool TrySellShuttle(EntityUid stationUid, EntityUid shuttleUid, out int bill)
    {
        bill = 0;

        if (!TryComp<StationDataComponent>(stationUid, out var stationData) || !HasComp<ShuttleComponent>(shuttleUid))
            return false;

        var targetGrid = _station.GetLargestGrid((stationUid, stationData));
        if (targetGrid == null)
            return false;

        if (!IsDockedTo(shuttleUid, targetGrid.Value))
        {
            Log.Info($"Shipyard: refusing to sell {ToPrettyString(shuttleUid)}, not docked to the station.");
            return false;
        }

        if (FoundOrganics(shuttleUid))
        {
            Log.Info($"Shipyard: refusing to sell {ToPrettyString(shuttleUid)}, someone is still aboard.");
            return false;
        }

        if (_station.GetOwningStation(shuttleUid) is { Valid: true } shuttleStation && shuttleStation != stationUid)
            _station.DeleteStation(shuttleStation);

        bill = (int) _pricing.AppraiseGrid(shuttleUid);
        Del(shuttleUid);
        Log.Info($"Shipyard: sold {shuttleUid} for {bill}");
        return true;
    }

    private bool IsDockedTo(EntityUid shuttleUid, EntityUid gridUid)
    {
        var gridDocks = _docking.GetDocks(gridUid);
        if (gridDocks.Count == 0)
            return false;

        foreach (var shuttleDock in _docking.GetDocks(shuttleUid))
        {
            if (shuttleDock.Comp.DockedWith is not { } dockedWith)
                continue;

            foreach (var gridDock in gridDocks)
            {
                if (gridDock.Owner == dockedWith)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when any living, IC-alive, minded mob is somewhere under <paramref name="uid"/>.
    /// </summary>
    public bool FoundOrganics(EntityUid uid)
    {
        var mobQuery = GetEntityQuery<MobStateComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();
        return FoundOrganics(uid, mobQuery, xformQuery);
    }

    private bool FoundOrganics(EntityUid uid, EntityQuery<MobStateComponent> mobQuery, EntityQuery<TransformComponent> xformQuery)
    {
        var xform = xformQuery.GetComponent(uid);
        var children = xform.ChildEnumerator;

        while (children.MoveNext(out var child))
        {
            if (mobQuery.TryGetComponent(child, out var mobState)
                && !_mobState.IsDead(child, mobState)
                && _mind.TryGetMind(child, out _, out var mindComp)
                && !_mind.IsCharacterDeadIc(mindComp))
                return true;

            if (FoundOrganics(child, mobQuery, xformQuery))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Renames the ship behind a deed and, if the ship is a station, the station too.
    /// </summary>
    public bool TryRenameShuttle(EntityUid uid, ShuttleDeedComponent? deed, string? newName, string? newSuffix)
    {
        if (!Resolve(uid, ref deed))
            return false;

        if (deed.ShuttleUid is not { } shuttle || !Exists(shuttle))
            return false;

        deed.ShuttleName = newName;
        deed.ShuttleNameSuffix = newSuffix;
        Dirty(uid, deed);

        var fullName = GetFullName(deed);
        _metaData.SetEntityName(shuttle, fullName);

        if (TryComp<ShuttleDeedComponent>(shuttle, out var shipDeed) && shipDeed != deed)
        {
            shipDeed.ShuttleName = newName;
            shipDeed.ShuttleNameSuffix = newSuffix;
            Dirty(shuttle, shipDeed);
        }

        if (_station.GetOwningStation(shuttle) is { Valid: true } station)
            _station.RenameStation(station, fullName, loud: false);

        return true;
    }

    private void CleanupShipyard()
    {
        if (ShipyardMapEntity == null)
            return;

        if (ShipyardMapId != null)
        {
            var query = EntityQueryEnumerator<MapGridComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                if (Transform(uid).MapID == ShipyardMapId)
                    Del(uid);
            }
        }

        _shuttleIndex = 0f;

        if (Exists(ShipyardMapEntity.Value))
            Del(ShipyardMapEntity.Value);

        ShipyardMapEntity = null;
        ShipyardMapId = null;
    }

    private void SetupShipyard()
    {
        if (ShipyardMapEntity != null && Exists(ShipyardMapEntity.Value))
            return;

        ShipyardMapEntity = _mapSystem.CreateMap(out var mapId);
        ShipyardMapId = mapId;
        _mapSystem.SetPaused(mapId, false);
    }
}
