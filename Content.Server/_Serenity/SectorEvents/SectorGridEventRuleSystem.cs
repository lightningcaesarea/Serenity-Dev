using Content.Server.Gravity;
using Content.Server.Station.Systems;
using Content.Server.StationEvents.Components;
using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Content.Shared.Gravity;
using Content.Shared.Mind.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Spawners.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Content.Server.GameTicking.Rules.Components;

namespace Content.Server._Serenity.SectorEvents;

/// <summary>
/// Loads the event grid when a sector event starts and takes it away again when it ends.
/// Anyone still aboard at the end is pulled back to a station spawn point; loot they carried off stays theirs.
/// </summary>
public sealed partial class SectorGridEventRuleSystem : StationEventSystem<SectorGridEventRuleComponent>
{
    private const int PlacementAttempts = 12;

    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedShuttleSystem _shuttle = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private GravitySystem _gravity = default!;

    private List<Entity<MapGridComponent>> _overlaps = new();

    protected override void Started(EntityUid uid, SectorGridEventRuleComponent comp, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, comp, gameRule, args);

        if (!TryComp<StationEventComponent>(uid, out var stationEvent))
            return;

        var station = stationEvent.TargetStation;
        if (station == null && !TryGetRandomStation(out station))
        {
            ForceEndSelf(uid, gameRule);
            return;
        }

        comp.Station = station;

        if (StationSystem.GetLargestGrid(station.Value) is not { } stationGrid ||
            !TryComp<MapGridComponent>(stationGrid, out var stationGridComp) ||
            comp.Paths.Count == 0)
        {
            Sawmill.Warning($"Sector event {ToPrettyString(uid)} has nowhere to put its grid.");
            ForceEndSelf(uid, gameRule);
            return;
        }

        var stationXform = Transform(stationGrid);
        var centre = _transform.GetWorldPosition(stationXform);
        var stationRadius = stationGridComp.LocalAABB.Size.Length() / 2f;
        var path = RobustRandom.Pick(comp.Paths);

        for (var attempt = 0; attempt < PlacementAttempts; attempt++)
        {
            var distance = stationRadius + RobustRandom.NextFloat(comp.MinimumDistance, comp.MaximumDistance);
            var position = centre + RobustRandom.NextAngle().ToVec() * distance;

            if (!_loader.TryLoadGrid(stationXform.MapID, path, out var grid, offset: position, rot: RobustRandom.NextAngle()))
            {
                Sawmill.Error($"Sector event grid {path} failed to load.");
                break;
            }

            if (attempt < PlacementAttempts - 1 && Overlaps(grid.Value))
            {
                Del(grid.Value.Owner);
                continue;
            }

            Prepare(comp, grid.Value.Owner);
            comp.Grids.Add(grid.Value.Owner);
            return;
        }

        ForceEndSelf(uid, gameRule);
    }

    private bool Overlaps(Entity<MapGridComponent> grid)
    {
        var xform = Transform(grid);
        var aabb = _transform.GetWorldMatrix(xform).TransformBox(grid.Comp.LocalAABB);
        _overlaps.Clear();
        _map.FindGridsIntersecting(xform.MapID, aabb, ref _overlaps);
        foreach (var other in _overlaps)
        {
            if (other.Owner != grid.Owner)
                return true;
        }

        return false;
    }

    private void Prepare(SectorGridEventRuleComponent rule, EntityUid grid)
    {
        if (rule.GridName is { } name)
            _meta.SetEntityName(grid, Loc.GetString(name));

        _shuttle.SetIFFColor(grid, rule.IffColor);

        var gravity = EnsureComp<GravityComponent>(grid);
        _gravity.EnableGravity(grid, gravity);

        // Event grids are places, not ships: nobody flies them away.
        RemComp<ShuttleComponent>(grid);
        if (TryComp<PhysicsComponent>(grid, out var physics))
            _physics.SetBodyType(grid, BodyType.Static, body: physics);
    }

    protected override void Ended(EntityUid uid, SectorGridEventRuleComponent comp, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, comp, gameRule, args);

        foreach (var grid in comp.Grids)
        {
            if (TerminatingOrDeleted(grid))
                continue;

            ReturnSurvivors(grid, comp.Station);
            Del(grid);
        }

        comp.Grids.Clear();
    }

    private void ReturnSurvivors(EntityUid grid, EntityUid? station)
    {
        EntityCoordinates? home = null;
        if (station != null)
        {
            var spawns = new List<EntityCoordinates>();
            var query = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
            while (query.MoveNext(out _, out var spawn, out var xform))
            {
                if (spawn.SpawnType == SpawnPointType.LateJoin && xform.GridUid is { } g && StationSystem.GetOwningStation(g) == station)
                    spawns.Add(xform.Coordinates);
            }

            if (spawns.Count > 0)
                home = RobustRandom.Pick(spawns);
        }

        if (home == null)
            return;

        var minds = new List<EntityUid>();
        var mobs = EntityQueryEnumerator<MindContainerComponent, TransformComponent>();
        while (mobs.MoveNext(out var mob, out var mind, out var xform))
        {
            if (mind.HasMind && xform.GridUid == grid)
                minds.Add(mob);
        }

        foreach (var mob in minds)
        {
            _transform.SetCoordinates(mob, home.Value);
        }
    }
}
