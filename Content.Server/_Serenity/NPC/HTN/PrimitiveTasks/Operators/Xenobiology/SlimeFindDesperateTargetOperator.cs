using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Serenity.Xenobiology;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared._Serenity.Xenobiology;
using Content.Shared.Interaction;

namespace Content.Server._Serenity.NPC.HTN.PrimitiveTasks.Operators.Xenobiology;

/// <summary>
/// Plans for a desperate slime: finds the nearest creature it may feed on and sets it as the target.
/// Unlike monkeys, these targets are never shared with the slime hive mind, so a desperate slime's prey
/// doesn't draw in the slimes that are still calm.
/// </summary>
public sealed partial class SlimeFindDesperateTargetOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;

    private SlimeTemperamentSystem _temperament = default!;
    private EntityLookupSystem _lookup = default!;
    private PathfindingSystem _pathfinding = default!;
    private SharedTransformSystem _transform = default!;

    /// <summary>
    /// Target entity to eat.
    /// </summary>
    [DataField(required: true)]
    public string TargetKey = string.Empty;

    /// <summary>
    /// Target coordinates to move to.
    /// </summary>
    [DataField(required: true)]
    public string TargetMoveKey = string.Empty;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _temperament = sysManager.GetEntitySystem<SlimeTemperamentSystem>();
        _lookup = sysManager.GetEntitySystem<EntityLookupSystem>();
        _pathfinding = sysManager.GetEntitySystem<PathfindingSystem>();
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<SlimeTemperamentComponent>(owner, out var temperament) ||
            !_temperament.IsDesperate(owner, temperament))
            return (false, null);

        var ownerPos = _transform.GetWorldPosition(owner);
        var candidates = _lookup.GetEntitiesInRange(owner, temperament.SearchRange)
            .Where(e => _temperament.IsDesperateTarget(temperament, e))
            .OrderBy(e => (_transform.GetWorldPosition(e) - ownerPos).LengthSquared())
            .ToList();

        foreach (var entity in candidates)
        {
            var path = await _pathfinding.GetPath(owner, entity, SharedInteractionSystem.InteractionRange - 1f, cancelToken);
            if (path.Result == PathResult.NoPath)
                continue;

            return (true, new Dictionary<string, object>
            {
                { TargetKey, entity },
                { TargetMoveKey, _entManager.GetComponent<TransformComponent>(entity).Coordinates },
                { NPCBlackboard.PathfindKey, path },
            });
        }

        return (false, null);
    }
}
