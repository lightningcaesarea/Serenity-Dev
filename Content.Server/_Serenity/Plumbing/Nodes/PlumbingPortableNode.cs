using Content.Server._Starlight.Plumbing.Nodes;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.NodeContainer;
using Robust.Shared.Map.Components;

namespace Content.Server._Serenity.Plumbing.Nodes;

/// <summary>
///     The node on a dockable tank. It has no pipe directions, so it never links to ducts directly:
///     the only thing it reaches is a <see cref="PlumbingPortNode"/> on the same tile.
///     A tank anchored anywhere else stays on a network of its own.
/// </summary>
[DataDefinition]
public sealed partial class PlumbingPortableNode : PlumbingNode
{
    public override IEnumerable<Node> GetReachableNodes(
        Entity<TransformComponent> xform,
        EntityQuery<NodeContainerComponent> nodeQuery,
        EntityQuery<TransformComponent> xformQuery,
        Entity<MapGridComponent>? grid,
        IEntityManager entMan)
    {
        if (!xform.Comp.Anchored || grid is not { } gridEnt)
            yield break;

        var mapSystem = entMan.System<SharedMapSystem>();
        var gridIndex = mapSystem.TileIndicesFor(gridEnt, xform.Comp.Coordinates);

        foreach (var node in NodeHelpers.GetNodesInTile(nodeQuery, gridEnt, gridIndex, mapSystem))
        {
            if (node is PlumbingPortNode)
                yield return node;
        }
    }
}
