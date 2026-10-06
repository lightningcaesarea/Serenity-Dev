using System.Diagnostics;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared._Serenity.CCVar;
using Content.Shared._Serenity.Flooding;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Maps;
using Content.Shared.Movement.Systems;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Serenity.Flooding;

/// <summary>
///     Moves flood water between tiles. Every active flood looks at its four neighbours (and anything docked to its
///     tile), and pours into the lower ones until they share one level. Airtight things hold the water back,
///     space swallows it, and a flood that ends up too shallow turns back into a puddle.
/// </summary>
public sealed partial class FloodSystem : SharedFloodSystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpeedModifierContactsSystem _speedContacts = default!;
    [Dependency] private TurfSystem _turf = default!;

    [Dependency] private EntityQuery<ActiveFloodComponent> _activeQuery = default!;
    [Dependency] private EntityQuery<AirtightComponent> _airtightQuery = default!;
    [Dependency] private EntityQuery<DockingComponent> _dockingQuery = default!;
    [Dependency] private EntityQuery<FloodComponent> _floodQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<PuddleComponent> _puddleQuery = default!;

    public static readonly EntProtoId FloodPrototype = "Flood";
    public static readonly EntProtoId SplashPrototype = "FloodSplash";

    private static readonly SoundSpecifier SplashSound =
        new SoundCollectionSpecifier("FloodWade", AudioParams.Default.WithVolume(-8f));

    private static readonly AtmosDirection[] Cardinals =
        [AtmosDirection.North, AtmosDirection.East, AtmosDirection.South, AtmosDirection.West];

    private bool _enabled;
    private TimeSpan _flowDelay;
    private TimeSpan _tickBudget;
    private FixedPoint2 _fromPuddle;
    private FixedPoint2 _minimum;
    private FixedPoint2 _tolerance;
    private FixedPoint2 _waist;
    private FixedPoint2 _chest;
    private FixedPoint2 _submerged;
    private FixedPoint2 _splash;

    private readonly Queue<EntityUid> _due = new();
    private readonly HashSet<EntityUid> _toPuddle = new();
    private readonly HashSet<EntityUid> _toFlood = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly List<Outlet> _outlets = new();
    private readonly List<Outlet> _receiving = new();
    private readonly List<EntityUid> _absorbed = new();

    /// <summary>
    ///     One tile water could flow to. <see cref="Void"/> means space: water poured there is gone.
    /// </summary>
    private record struct Outlet(EntityUid Grid, MapGridComponent GridComp, Vector2i Indices, bool Void)
    {
        public Entity<FloodComponent>? Flood;
        public FixedPoint2 Level;
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FloodComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FloodComponent, SolutionChangedEvent>(OnSolutionChanged);
        SubscribeLocalEvent<FloodComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<AirtightChanged>(OnAirtightChanged);

        InitializeBreathing();
        InitializeDrains();

        Subs.CVar(_config, SerenityCCVars.FloodEnabled, v => _enabled = v, true);
        Subs.CVar(_config, SerenityCCVars.FloodFlowDelay, v => _flowDelay = TimeSpan.FromSeconds(v), true);
        Subs.CVar(_config, SerenityCCVars.FloodTickBudget, v => _tickBudget = TimeSpan.FromMilliseconds(v), true);
        Subs.CVar(_config, SerenityCCVars.FloodFromPuddle, v => _fromPuddle = FixedPoint2.New(v), true);
        Subs.CVar(_config, SerenityCCVars.FloodMinimum, v => _minimum = FixedPoint2.New(v), true);
        Subs.CVar(_config, SerenityCCVars.FloodLevelTolerance, v => _tolerance = FixedPoint2.New(v), true);
        Subs.CVar(_config, SerenityCCVars.FloodWaistDepth, v => _waist = FixedPoint2.New(v), true);
        Subs.CVar(_config, SerenityCCVars.FloodChestDepth, v => _chest = FixedPoint2.New(v), true);
        Subs.CVar(_config, SerenityCCVars.FloodSubmergedDepth, v => _submerged = FixedPoint2.New(v), true);
        Subs.CVar(_config, SerenityCCVars.FloodSplashUnits, v => _splash = FixedPoint2.New(v), true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        var now = _timing.CurTime;
        ConvertPuddles();
        UpdateDrains(now);

        if (_due.Count == 0)
        {
            var query = EntityQueryEnumerator<ActiveFloodComponent, FloodComponent>();
            while (query.MoveNext(out var uid, out var active, out _))
            {
                if (active.NextFlow <= now)
                    _due.Enqueue(uid);
            }
        }

        // Anything left over when the budget runs out keeps its place in the queue for the next tick.
        _stopwatch.Restart();
        while (_stopwatch.Elapsed < _tickBudget && _due.TryDequeue(out var uid))
        {
            if (TerminatingOrDeleted(uid)
                || !_activeQuery.TryComp(uid, out var active)
                || !_floodQuery.TryComp(uid, out var flood))
            {
                continue;
            }

            Flow((uid, flood), active, now);
        }

        RevertShallowFloods();
    }

    #region Public API

    /// <summary>
    ///     Finds the flood on a tile, if there is one.
    /// </summary>
    public bool TryGetFlood(EntityUid grid, MapGridComponent gridComp, Vector2i indices, out Entity<FloodComponent> flood)
    {
        var anchored = _map.GetAnchoredEntities(grid, gridComp, indices);
        while (anchored.MoveNext(out var ent))
        {
            if (!_floodQuery.TryComp(ent, out var comp))
                continue;

            flood = (ent.Value, comp);
            return true;
        }

        flood = default;
        return false;
    }

    /// <summary>
    ///     Pours a solution into the flood already on a tile. Returns false, leaving the solution alone, if that tile
    ///     has no flood. Used so spills onto flooded tiles join the water instead of floating a puddle on top.
    /// </summary>
    public bool TryAddToFlood(TileRef tile, Solution solution, out EntityUid flood)
    {
        flood = EntityUid.Invalid;
        if (!_gridQuery.TryComp(tile.GridUid, out var grid)
            || !TryGetFlood(tile.GridUid, grid, tile.GridIndices, out var found))
        {
            return false;
        }

        AddToFlood(found, solution);
        flood = found;
        return true;
    }

    /// <summary>
    ///     Pours a solution onto a tile as flood water, starting a new flood there if needed.
    ///     Too little to count as a flood and it settles as a puddle instead.
    /// </summary>
    public bool TryFloodAt(EntityCoordinates coordinates, Solution solution, out EntityUid flood)
    {
        flood = EntityUid.Invalid;
        if (_transform.GetGrid(coordinates) is not { } gridUid || !_gridQuery.TryComp(gridUid, out var grid))
            return false;

        var indices = _map.TileIndicesFor(gridUid, grid, coordinates);
        if (!_map.TryGetTileRef(gridUid, grid, indices, out var tileRef) || tileRef.Tile.IsEmpty || _turf.IsSpace(tileRef))
            return false;

        if (!TryGetFlood(gridUid, grid, indices, out var found))
            found = CreateFlood(gridUid, grid, indices);

        AddToFlood(found, solution);
        flood = found;
        return true;
    }

    /// <summary>
    ///     Called when a puddle's solution changes. A puddle that got deep enough becomes a flood on the next tick.
    /// </summary>
    public void CheckPuddle(EntityUid puddle, Solution solution)
    {
        if (_enabled && solution.Volume >= _fromPuddle)
            _toFlood.Add(puddle);
    }

    public FloodDepth GetDepth(FixedPoint2 volume)
    {
        if (volume >= _submerged)
            return FloodDepth.Submerged;

        if (volume >= _chest)
            return FloodDepth.Chest;

        return volume >= _waist ? FloodDepth.Waist : FloodDepth.Ankles;
    }

    #endregion

    private void OnMapInit(Entity<FloodComponent> ent, ref MapInitEvent args)
    {
        var xform = Transform(ent);
        if (xform.GridUid is { } gridUid && _gridQuery.TryComp(gridUid, out var grid))
        {
            // Two floods on one tile, say from mapping: fold this one into the one that was already there.
            var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            var anchored = _map.GetAnchoredEntities(gridUid, grid, indices);
            while (anchored.MoveNext(out var other))
            {
                if (other == ent.Owner || !_floodQuery.TryComp(other, out var otherFlood))
                    continue;

                if (_solution.ResolveSolution(ent.Owner, ent.Comp.Solution, ref ent.Comp.SolutionEntity, out var ours))
                    AddToFlood((other.Value, otherFlood), ours.Clone());

                QueueDel(ent);
                return;
            }
        }

        if (_solution.ResolveSolution(ent.Owner, ent.Comp.Solution, ref ent.Comp.SolutionEntity, out var solution))
        {
            ent.Comp.LastVolume = solution.Volume;
            Refresh(ent, solution);

            // A freshly made flood is filled straight after spawning; one that is still this shallow by the end
            // of the tick goes back to being a puddle.
            if (solution.Volume < _minimum)
                _toPuddle.Add(ent);
        }

        Activate(ent);
    }

    private void OnSolutionChanged(Entity<FloodComponent> ent, ref SolutionChangedEvent args)
    {
        if (args.Solution.Comp.Id != ent.Comp.Solution)
            return;

        var volume = args.Solution.Comp.Solution.Volume;
        var previous = ent.Comp.LastVolume;
        ent.Comp.LastVolume = volume;

        if (volume < _minimum)
        {
            _toPuddle.Add(ent);
            return;
        }

        _toPuddle.Remove(ent);
        Refresh(ent, args.Solution.Comp.Solution);

        // Rising water pushes outward. Falling water lets the higher neighbours pour back in.
        if (volume > previous)
            Activate(ent);
        else if (volume < previous)
            WakeNeighbours(ent);
    }

    private void OnTerminating(Entity<FloodComponent> ent, ref EntityTerminatingEvent args)
    {
        _toPuddle.Remove(ent);
        WakeNeighbours(ent);
    }

    private void OnAirtightChanged(ref AirtightChanged ev)
    {
        var (gridUid, indices) = ev.Position;
        if (TerminatingOrDeleted(gridUid) || !_gridQuery.TryComp(gridUid, out var grid))
            return;

        WakeAround(gridUid, grid, indices);
    }

    private void Flow(Entity<FloodComponent> ent, ActiveFloodComponent active, TimeSpan now)
    {
        var xform = Transform(ent);
        if (!_solution.ResolveSolution(ent.Owner, ent.Comp.Solution, ref ent.Comp.SolutionEntity, out var solution)
            || xform.GridUid is not { } gridUid
            || !_gridQuery.TryComp(gridUid, out var grid))
        {
            RemCompDeferred<ActiveFloodComponent>(ent);
            return;
        }

        var volume = solution.Volume;
        var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);

        _outlets.Clear();
        CollectOutlets(gridUid, grid, indices, _outlets);

        _receiving.Clear();
        foreach (var outlet in _outlets)
        {
            var open = outlet;
            open.Level = outlet.Void ? FixedPoint2.Zero : LevelAt(ref open);

            if (volume - open.Level > _tolerance)
                _receiving.Add(open);
        }

        var level = SharedLevel(volume);
        if (_receiving.Count == 0)
        {
            RemCompDeferred<ActiveFloodComponent>(ent);
            return;
        }

        Color? color = null;
        foreach (var outlet in _receiving)
        {
            var amount = level - outlet.Level;
            if (amount <= FixedPoint2.Zero)
                continue;

            var portion = _solution.SplitSolution(ent.Comp.SolutionEntity.Value, amount);

            // Poured into space.
            if (outlet.Void)
                continue;

            var target = outlet.Flood ?? CreateFlood(outlet.Grid, outlet.GridComp, outlet.Indices);
            if (amount >= _splash)
            {
                color ??= portion.GetColor(_prototype);
                Splash(outlet.Grid, outlet.GridComp, outlet.Indices, color.Value);
            }

            AddToFlood(target, portion);
        }

        active.NextFlow = now + _flowDelay;
    }

    /// <summary>
    ///     Works out the level this tile and its lower neighbours settle at, trimming <see cref="_receiving"/> down to
    ///     the neighbours that actually take water.
    /// </summary>
    private FixedPoint2 SharedLevel(FixedPoint2 volume)
    {
        while (true)
        {
            if (_receiving.Count == 0)
                return volume;

            var total = volume;
            var highest = 0;
            for (var i = 0; i < _receiving.Count; i++)
            {
                total += _receiving[i].Level;
                if (_receiving[i].Level > _receiving[highest].Level)
                    highest = i;
            }

            var level = total / FixedPoint2.New(_receiving.Count + 1);

            // A neighbour already at or above the shared level would only give water back.
            if (_receiving[highest].Level >= level)
            {
                _receiving.RemoveAt(highest);
                continue;
            }

            if (level >= _minimum)
                return level;

            // Too shallow to cover every dry tile: drop one dry tile and try again, so the edge of the water
            // stays deep enough to be a flood instead of breaking up into puddles.
            var dry = _receiving.FindIndex(o => !o.Void && o.Flood == null);
            if (dry < 0)
                return level;

            _receiving.RemoveAt(dry);
        }
    }

    /// <summary>
    ///     Volume already standing on an outlet tile: its flood, or failing that its puddle.
    /// </summary>
    private FixedPoint2 LevelAt(ref Outlet outlet)
    {
        var level = FixedPoint2.Zero;
        var anchored = _map.GetAnchoredEntities(outlet.Grid, outlet.GridComp, outlet.Indices);
        while (anchored.MoveNext(out var ent))
        {
            if (_floodQuery.TryComp(ent, out var flood))
            {
                outlet.Flood = (ent.Value, flood);
                return _solution.ResolveSolution(ent.Value, flood.Solution, ref flood.SolutionEntity, out var floodSolution)
                    ? floodSolution.Volume
                    : FixedPoint2.Zero;
            }

            if (_puddleQuery.TryComp(ent, out var puddle)
                && _solution.ResolveSolution(ent.Value, puddle.SolutionName, ref puddle.Solution, out var puddleSolution))
            {
                level += puddleSolution.Volume;
            }
        }

        return level;
    }

    /// <summary>
    ///     Every tile water on this tile can reach: the four neighbours that nothing airtight closes off, plus the
    ///     tile across any dock sitting here.
    /// </summary>
    private void CollectOutlets(EntityUid gridUid, MapGridComponent grid, Vector2i indices, List<Outlet> outlets)
    {
        var blocked = AtmosDirection.Invalid;
        var anchored = _map.GetAnchoredEntities(gridUid, grid, indices);
        while (anchored.MoveNext(out var ent))
        {
            if (_airtightQuery.TryComp(ent, out var airtight) && airtight.AirBlocked)
                blocked |= airtight.AirBlockedDirection;

            if (_dockingQuery.TryComp(ent, out var dock)
                && dock.DockedWith is { } dockedWith
                && TryComp(dockedWith, out TransformComponent? dockedXform)
                && dockedXform.GridUid is { } dockedGrid
                && _gridQuery.TryComp(dockedGrid, out var dockedGridComp))
            {
                var ourDir = Transform(ent.Value).LocalRotation.ToAtmosDirection();
                var theirDir = dockedXform.LocalRotation.ToAtmosDirection();
                var dockedIndices = _map.TileIndicesFor(dockedGrid, dockedGridComp, dockedXform.Coordinates);
                if ((blocked & ourDir) == 0 && !BlocksFrom(dockedGrid, dockedGridComp, dockedIndices, theirDir))
                    outlets.Add(new Outlet(dockedGrid, dockedGridComp, dockedIndices, false));
            }
        }

        for (var i = 0; i < Cardinals.Length; i++)
        {
            var dir = Cardinals[i];
            if ((blocked & dir) != 0)
                continue;

            var neighbour = indices.Offset(dir);
            if (!_map.TryGetTileRef(gridUid, grid, neighbour, out var tileRef)
                || tileRef.Tile.IsEmpty
                || _turf.IsSpace(tileRef))
            {
                outlets.Add(new Outlet(gridUid, grid, neighbour, true));
                continue;
            }

            if (BlocksFrom(gridUid, grid, neighbour, dir.GetOpposite()))
                continue;

            outlets.Add(new Outlet(gridUid, grid, neighbour, false));
        }
    }

    /// <summary>
    ///     Whether something airtight on a tile closes the given side of it.
    /// </summary>
    private bool BlocksFrom(EntityUid gridUid, MapGridComponent grid, Vector2i indices, AtmosDirection side)
    {
        var anchored = _map.GetAnchoredEntities(gridUid, grid, indices);
        while (anchored.MoveNext(out var ent))
        {
            if (_airtightQuery.TryComp(ent, out var airtight)
                && airtight.AirBlocked
                && (airtight.AirBlockedDirection & side) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private Entity<FloodComponent> CreateFlood(EntityUid gridUid, MapGridComponent grid, Vector2i indices)
    {
        var uid = Spawn(FloodPrototype, _map.GridTileToLocal(gridUid, grid, indices));
        var flood = _floodQuery.Comp(uid);

        // Soak up whatever puddle was already there.
        _absorbed.Clear();
        var anchored = _map.GetAnchoredEntities(gridUid, grid, indices);
        while (anchored.MoveNext(out var ent))
        {
            if (_puddleQuery.HasComp(ent))
                _absorbed.Add(ent.Value);
        }

        foreach (var puddleUid in _absorbed)
        {
            var puddle = _puddleQuery.Comp(puddleUid);
            if (_solution.ResolveSolution(puddleUid, puddle.SolutionName, ref puddle.Solution, out var puddleSolution))
                AddToFlood((uid, flood), puddleSolution.Clone());

            Del(puddleUid);
        }

        return (uid, flood);
    }

    private void AddToFlood(Entity<FloodComponent> flood, Solution solution)
    {
        if (_solution.ResolveSolution(flood.Owner, flood.Comp.Solution, ref flood.Comp.SolutionEntity))
            _solution.AddSolution(flood.Comp.SolutionEntity.Value, solution);
    }

    /// <summary>
    ///     Brings depth, look and wading speed in line with the volume.
    /// </summary>
    private void Refresh(Entity<FloodComponent> ent, Solution solution)
    {
        var depth = GetDepth(solution.Volume);
        _appearance.SetData(ent, FloodVisuals.Color, solution.GetColor(_prototype));

        if (depth == ent.Comp.Depth && _appearance.TryGetData<FloodDepth>(ent, FloodVisuals.Depth, out _))
            return;

        ent.Comp.Depth = depth;
        Dirty(ent);
        _appearance.SetData(ent, FloodVisuals.Depth, depth);

        if (ent.Comp.SpeedModifiers.TryGetValue(depth, out var speed))
            _speedContacts.ChangeSpeedModifiers(ent, speed, speed);
    }

    private void Splash(EntityUid gridUid, MapGridComponent grid, Vector2i indices, Color color)
    {
        var coordinates = _map.GridTileToLocal(gridUid, grid, indices);
        var splash = Spawn(SplashPrototype, coordinates);
        _appearance.SetData(splash, FloodVisuals.Color, color);

        if (_random.Prob(0.1f))
            _audio.PlayPvs(SplashSound, coordinates);
    }

    private void Activate(EntityUid uid)
    {
        if (_activeQuery.HasComp(uid) || TerminatingOrDeleted(uid))
            return;

        var active = AddComp<ActiveFloodComponent>(uid);
        active.NextFlow = _timing.CurTime + _flowDelay;
    }

    private void WakeNeighbours(EntityUid uid)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } gridUid
            || TerminatingOrDeleted(gridUid)
            || !_gridQuery.TryComp(gridUid, out var grid))
        {
            return;
        }

        WakeAround(gridUid, grid, _map.TileIndicesFor(gridUid, grid, xform.Coordinates));
    }

    /// <summary>
    ///     Wakes the floods on a tile and its four neighbours.
    /// </summary>
    private void WakeAround(EntityUid gridUid, MapGridComponent grid, Vector2i indices)
    {
        if (TryGetFlood(gridUid, grid, indices, out var here))
            Activate(here);

        foreach (var dir in Cardinals)
        {
            if (TryGetFlood(gridUid, grid, indices.Offset(dir), out var neighbour))
                Activate(neighbour);
        }
    }

    private void ConvertPuddles()
    {
        if (_toFlood.Count == 0)
            return;

        // Making a flood deletes the puddles it soaks up, so work from a copy.
        var puddles = new List<EntityUid>(_toFlood);
        _toFlood.Clear();

        foreach (var puddleUid in puddles)
        {
            if (TerminatingOrDeleted(puddleUid) || !_puddleQuery.TryComp(puddleUid, out var puddle))
                continue;

            if (!_solution.ResolveSolution(puddleUid, puddle.SolutionName, ref puddle.Solution, out var solution)
                || solution.Volume < _fromPuddle)
            {
                continue;
            }

            var xform = Transform(puddleUid);
            if (xform.GridUid is not { } gridUid || !_gridQuery.TryComp(gridUid, out var grid))
                continue;

            var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            if (TryGetFlood(gridUid, grid, indices, out var existing))
            {
                AddToFlood(existing, solution.Clone());
                Del(puddleUid);
                continue;
            }

            // The new flood soaks this puddle up.
            CreateFlood(gridUid, grid, indices);
        }
    }

    private void RevertShallowFloods()
    {
        if (_toPuddle.Count == 0)
            return;

        // Deleting a flood wakes its neighbours, which can queue more shallow floods, so work from a copy.
        var shallow = new List<EntityUid>(_toPuddle);
        _toPuddle.Clear();

        foreach (var uid in shallow)
        {
            if (TerminatingOrDeleted(uid) || !_floodQuery.TryComp(uid, out var flood))
                continue;

            var coordinates = Transform(uid).Coordinates;
            Solution? leftover = null;
            if (_solution.ResolveSolution(uid, flood.Solution, ref flood.SolutionEntity, out var solution))
                leftover = solution.Clone();

            Del(uid);

            if (leftover != null && leftover.Volume > FixedPoint2.Zero)
                _puddle.TrySpillAt(coordinates, leftover, out _, false);
        }
    }
}
