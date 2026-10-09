using System.Numerics;
using Content.Server.Shuttles.Systems;
using Content.Shared._Serenity.CCVar;
using Content.Shared._Serenity.Docks;
using Content.Shared._Serenity.Shipyard.Components;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Content.Shared.Physics;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Serenity.Docks;

/// <summary>
/// Lets players call in extra docking piers. Any shipyard console gets a "Request dock" verb that
/// loads a premade dock grid a fixed distance out from the console's grid, as long as the
/// per-round limit and the cooldown allow it and the spot is clear of every other grid.
/// </summary>
/// <remarks>
/// Written for Serenity. The idea (a button that loads a dock grid into space) exists in other
/// forks, but no code from them is used.
/// </remarks>
public sealed partial class SpawnableDockSystem : EntitySystem
{
    /// <summary>Where the dock grid lives. Its origin is the centre of the pier.</summary>
    public static readonly ResPath DockPath = new("/Maps/_Serenity/Docks/dock.yml");

    /// <summary>
    /// Half-width, in tiles, of the square that must be free of other grids before a dock is loaded.
    /// The dock (power room, hall and atmos room) is 10 wide and 56 long and spans 25 tiles south and
    /// 31 north of its origin, so this keeps every tile of it inside the checked square.
    /// </summary>
    public const float ClearanceRadius = 36f;

    /// <summary>How many evenly spaced bearings are tried before giving up.</summary>
    public const int Bearings = 16;

    private static readonly Color DockIffColor = Color.FromHex("#5FA8D3");

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private DockTeleporterSystem _teleporter = default!;
    [Dependency] private TurfSystem _turf = default!;

    private readonly List<EntityUid> _docks = new();
    private TimeSpan _nextSpawn;
    private int _counter;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShipyardConsoleComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
    }

    private void Reset()
    {
        _docks.Clear();
        _nextSpawn = TimeSpan.Zero;
        _counter = 0;
    }

    private void OnGetVerbs(Entity<ShipyardConsoleComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !_cfg.GetCVar(SerenityCCVars.DocksEnabled))
            return;

        var console = ent.Owner;
        var user = args.User;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("dock-request-verb"),
            Message = Loc.GetString("dock-request-verb-message"),
            Act = () => TryRequestDock(console, user),
        });
    }

    /// <summary>
    /// Checks the limits and, if they allow it, spawns a dock. Tells <paramref name="user"/> what happened.
    /// </summary>
    public bool TryRequestDock(EntityUid console, EntityUid user)
    {
        if (!_cfg.GetCVar(SerenityCCVars.DocksEnabled))
        {
            _popup.PopupEntity(Loc.GetString("dock-request-disabled"), console, user);
            return false;
        }

        // Docks that were deleted (admin cleanup, round-end deletion) free their slot.
        _docks.RemoveAll(d => !Exists(d) || TerminatingOrDeleted(d));

        var max = _cfg.GetCVar(SerenityCCVars.DocksMax);
        if (_docks.Count >= max)
        {
            _popup.PopupEntity(Loc.GetString("dock-request-limit", ("max", max)), console, user);
            return false;
        }

        var now = _timing.CurTime;
        if (now < _nextSpawn)
        {
            var seconds = (int) Math.Ceiling((_nextSpawn - now).TotalSeconds);
            _popup.PopupEntity(Loc.GetString("dock-request-cooldown", ("seconds", seconds)), console, user);
            return false;
        }

        var xform = Transform(console);
        if (xform.GridUid is not { } hostGrid || xform.MapID == MapId.Nullspace)
        {
            _popup.PopupEntity(Loc.GetString("dock-request-failed"), console, user);
            return false;
        }

        var origin = _transform.GetWorldPosition(hostGrid);
        var distance = _cfg.GetCVar(SerenityCCVars.DocksDistance);

        if (!TryFindClearBearing(xform.MapID, origin, distance, out var position))
        {
            _popup.PopupEntity(Loc.GetString("dock-request-no-space"), console, user);
            return false;
        }

        if (SpawnDock(xform.MapID, position) is not { } dock)
        {
            _popup.PopupEntity(Loc.GetString("dock-request-failed"), console, user);
            return false;
        }

        LinkTeleporters(console, dock);

        _nextSpawn = now + TimeSpan.FromSeconds(_cfg.GetCVar(SerenityCCVars.DocksCooldown));
        _popup.PopupEntity(Loc.GetString("dock-request-success", ("number", _counter)), console, user);
        Log.Info($"{ToPrettyString(user)} requested a dock from {ToPrettyString(console)}; {ToPrettyString(dock)} placed at {position}.");
        return true;
    }

    /// <summary>
    /// Puts a teleporter pad on a free tile near <paramref name="console"/> and links it to the pad in the
    /// middle of <paramref name="dock"/>, so people can get to the pier without a ship.
    /// </summary>
    private void LinkTeleporters(EntityUid console, EntityUid dock)
    {
        Entity<DockTeleporterComponent>? dockPad = null;
        var query = EntityQueryEnumerator<DockTeleporterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid == dock)
            {
                dockPad = (uid, comp);
                break;
            }
        }

        if (dockPad is not { } pier)
            return;

        var consoleXform = Transform(console);
        if (consoleXform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var centre = _map.TileIndicesFor(grid, gridComp, consoleXform.Coordinates);
        for (var radius = 1; radius <= 4; radius++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
                        continue;

                    var tile = _map.GetTileRef(grid, gridComp, centre + new Vector2i(dx, dy));
                    if (_turf.IsSpace(tile) || _turf.IsTileBlocked(tile, CollisionGroup.MobMask))
                        continue;

                    var pad = Spawn("SerenityDockTeleporter", _map.GridTileToLocal(grid, gridComp, tile.GridIndices));
                    if (TryComp<DockTeleporterComponent>(pad, out var padComp))
                        _teleporter.Link((pad, padComp), pier);
                    return;
                }
            }
        }

        Log.Warning($"No free tile near {ToPrettyString(console)} for a dock teleporter pad.");
    }

    /// <summary>
    /// Walks around a circle of radius <paramref name="distance"/> centred on <paramref name="origin"/>,
    /// starting at a random bearing, and returns the first point whose surroundings hold no grid.
    /// </summary>
    private bool TryFindClearBearing(MapId map, Vector2 origin, float distance, out Vector2 position)
    {
        var start = _random.NextAngle();
        var step = Angle.FromDegrees(360.0 / Bearings);

        for (var i = 0; i < Bearings; i++)
        {
            var candidate = origin + (start + step * i).ToVec() * distance;
            if (IsClear(map, candidate, null))
            {
                position = candidate;
                return true;
            }
        }

        position = default;
        return false;
    }

    /// <summary>
    /// Whether the square of <see cref="ClearanceRadius"/> around <paramref name="centre"/> overlaps any grid
    /// other than <paramref name="ignore"/>.
    /// </summary>
    private bool IsClear(MapId map, Vector2 centre, EntityUid? ignore)
    {
        var box = Box2.CenteredAround(centre, new Vector2(ClearanceRadius * 2f, ClearanceRadius * 2f));
        var grids = new List<Entity<MapGridComponent>>();
        _map.FindGridsIntersecting(map, box, ref grids, approx: false, includeMap: false);

        foreach (var grid in grids)
        {
            if (grid.Owner != ignore)
                return false;
        }

        return true;
    }

    private EntityUid? SpawnDock(MapId map, Vector2 position)
    {
        if (!_loader.TryLoadGrid(map, DockPath, out var grid, offset: position))
        {
            Log.Error($"Failed to load the dock grid from {DockPath}.");
            return null;
        }

        var dock = grid.Value.Owner;

        // Belt and braces: the pre-check used a fixed radius, so also test the loaded grid's real bounds.
        var bounds = _transform.GetWorldMatrix(dock).TransformBox(grid.Value.Comp.LocalAABB);
        var overlapping = new List<Entity<MapGridComponent>>();
        _map.FindGridsIntersecting(map, bounds, ref overlapping, approx: false, includeMap: false);
        foreach (var other in overlapping)
        {
            if (other.Owner == dock)
                continue;

            Log.Warning($"Dock {ToPrettyString(dock)} overlapped {ToPrettyString(other.Owner)} after loading; removing it.");
            QueueDel(dock);
            return null;
        }

        _counter++;
        _meta.SetEntityName(dock, Loc.GetString("dock-name", ("number", _counter)));
        _shuttle.SetIFFColor(dock, DockIffColor);
        _docks.Add(dock);
        return dock;
    }
}
