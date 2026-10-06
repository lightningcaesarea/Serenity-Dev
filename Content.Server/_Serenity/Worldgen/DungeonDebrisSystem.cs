using Content.Server.Procedural;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Serenity.Worldgen;

/// <summary>
/// Fills <see cref="DungeonDebrisComponent"/> grids with their dungeon as soon as the worldgen debris placer
/// spawns them. This is what lets the station's old "wreck" dungeons be rolled by the worldgen biome like any
/// other debris, instead of being placed once in a fixed ring around the station.
/// </summary>
public sealed partial class DungeonDebrisSystem : EntitySystem
{
    [Dependency] private DungeonSystem _dungeon = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DungeonDebrisComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<DungeonDebrisComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<MapGridComponent>(ent, out var grid))
        {
            Log.Error($"Dungeon debris {ToPrettyString(ent)} has no grid to generate '{ent.Comp.Config}' on.");
            return;
        }

        if (!_proto.Resolve(ent.Comp.Config, out var config))
            return;

        _dungeon.GenerateDungeon(config, ent, grid, Vector2i.Zero, _random.Next());
    }
}
