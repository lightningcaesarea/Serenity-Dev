using Content.Server._Starlight.Plumbing.Nodes;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.NodeContainer;
using Robust.Shared.Map.Components;

namespace Content.Server._Serenity.Plumbing.Nodes;

/// <summary>
///     The node on a fluid connector port. Connects to ducts like any plumbing machine,
///     plus any anchored <see cref="PlumbingPortableNode"/> on the same tile.
/// </summary>
[DataDefinition]
public sealed partial class PlumbingPortNode : PlumbingNode
{
    public override IEnumerable<Node> GetReachableNodes(
        Entity<TransformComponent> xform,
        EntityQuery<NodeContainerComponent> nodeQuery,
        EntityQuery<TransformComponent> xformQuery,
        Entity<MapGridComponent>? grid,
        IEntityManager entMan)
    {
        if (xform.Comp.Anchored && grid is { } gridEnt)
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            var gridIndex = mapSystem.TileIndicesFor(gridEnt, xform.Comp.Coordinates);

            foreach (var node in NodeHelpers.GetNodesInTile(nodeQuery, gridEnt, gridIndex, mapSystem))
            {
                if (node is PlumbingPortableNode)
                    yield return node;
            }
        }

        foreach (var node in base.GetReachableNodes(xform, nodeQuery, xformQuery, grid, entMan))
        {
            yield return node;
        }
    }
}
