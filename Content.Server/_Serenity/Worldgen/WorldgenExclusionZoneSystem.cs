using System.Numerics;
using Content.Server.Worldgen.Components.Debris;
using Content.Server.Worldgen.Systems.Debris;
using Robust.Shared.Map.Components;

namespace Content.Server._Serenity.Worldgen;

/// <summary>
/// Cancels worldgen debris placement near grids carrying <see cref="WorldgenExclusionZoneComponent"/>.
/// </summary>
public sealed partial class WorldgenExclusionZoneSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DebrisFeaturePlacerControllerComponent, PrePlaceDebrisFeatureEvent>(OnPrePlaceDebris);
    }

    private void OnPrePlaceDebris(Entity<DebrisFeaturePlacerControllerComponent> ent, ref PrePlaceDebrisFeatureEvent args)
    {
        if (args.Handled)
            return;

        var point = _transform.ToMapCoordinates(args.Coords);
        var query = EntityQueryEnumerator<WorldgenExclusionZoneComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out _, out var zone, out var grid, out var xform))
        {
            if (xform.MapID != point.MapId)
                continue;

            var local = Vector2.Transform(point.Position, _transform.GetInvWorldMatrix(xform));
            var box = grid.LocalAABB;
            var dx = MathF.Max(MathF.Max(box.Left - local.X, local.X - box.Right), 0f);
            var dy = MathF.Max(MathF.Max(box.Bottom - local.Y, local.Y - box.Top), 0f);

            if (dx * dx + dy * dy >= zone.Radius * zone.Radius)
                continue;

            args.Handled = true;
            return;
        }
    }
}
