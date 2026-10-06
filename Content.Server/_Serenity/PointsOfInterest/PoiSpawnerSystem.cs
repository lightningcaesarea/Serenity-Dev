// Design inspired by Frontier Station 14's MIT-era NfAdventureRuleSystem (5be37d18c2, 2024-07-01):
// same idea (load grids onto the default map at round start, at a random offset, optionally as a
// station), reimplemented data-driven against SerenityPointOfInterestPrototype instead of their
// hardcoded per-POI blocks, and against the current engine's MapLoaderSystem/MapSystem APIs.

using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Maps;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Serenity.CCVar;
using Content.Shared._Serenity.PointsOfInterest;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Serenity.PointsOfInterest;

/// <summary>
/// Loads every enabled <see cref="SerenityPointOfInterestPrototype"/> onto the default map at round
/// start, each at a random point within its own distance range.
/// </summary>
public sealed partial class PoiSpawnerSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private StationSystem _station = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        if (!_cfg.GetCVar(SerenityCCVars.PoiSpawnerEnabled))
            return;

        var mapId = _ticker.DefaultMap;

        foreach (var poi in _proto.EnumeratePrototypes<SerenityPointOfInterestPrototype>())
        {
            if (!poi.Enabled)
                continue;

            SpawnPoi(mapId, poi);
        }
    }

    private void SpawnPoi(Robust.Shared.Map.MapId mapId, SerenityPointOfInterestPrototype poi)
    {
        var offset = _random.NextVector2(poi.MinimumDistance, poi.MaximumDistance);

        if (!_loader.TryLoadGrid(mapId, poi.GridPath, out var grid, offset: offset))
        {
            Log.Error($"Point of interest '{poi.ID}' failed to load from {poi.GridPath}.");
            return;
        }

        var gridUid = grid.Value.Owner;

        _meta.SetEntityName(gridUid, poi.Name.Length > 0 ? poi.Name : poi.ID);
        _shuttle.SetIFFColor(gridUid, poi.IffColor);
        if (poi.HideLabel)
            _shuttle.AddIFFFlag(gridUid, Content.Shared.Shuttles.Components.IFFFlags.HideLabel);

        if (poi.StationMap is { } stationMapId
            && _proto.TryIndex(stationMapId, out var gameMap)
            && gameMap.Stations.TryGetValue(stationMapId, out var stationConfig))
        {
            _station.InitializeNewStation(stationConfig, new[] { gridUid });
        }

        Log.Info($"Point of interest '{poi.ID}' spawned at {offset} on map {mapId}.");
    }
}
