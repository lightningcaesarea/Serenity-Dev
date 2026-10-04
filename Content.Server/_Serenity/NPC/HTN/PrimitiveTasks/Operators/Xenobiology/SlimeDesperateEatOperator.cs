using Content.Server._Serenity.Xenobiology;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared._Serenity.Xenobiology;
using Content.Shared._Starlight.Xenobiology;

namespace Content.Server._Serenity.NPC.HTN.PrimitiveTasks.Operators.Xenobiology;

/// <summary>
/// One bite from a desperate slime. Fails once the slime has eaten enough to calm down, or once the target is
/// dead or has taken <see cref="SlimeTemperamentComponent.DamageCap"/> damage.
/// </summary>
public sealed partial class SlimeDesperateEatOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;

    private SlimeSystem _slime = default!;
    private SlimeTemperamentSystem _temperament = default!;

    /// <summary>
    /// Target entity to eat.
    /// </summary>
    [DataField(required: true)]
    public string TargetKey = string.Empty;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _slime = sysManager.GetEntitySystem<SlimeSystem>();
        _temperament = sysManager.GetEntitySystem<SlimeTemperamentSystem>();
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        blackboard.Remove<EntityUid>(TargetKey);
        base.TaskShutdown(blackboard, status);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<SlimeComponent>(owner, out var slime) ||
            !_entManager.TryGetComponent<SlimeTemperamentComponent>(owner, out var temperament))
            return HTNOperatorStatus.Failed;

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager) || _entManager.Deleted(target))
            return HTNOperatorStatus.Failed;

        if (!_temperament.IsDesperate(owner, temperament) || !_temperament.IsDesperateTarget(temperament, target))
            return HTNOperatorStatus.Failed;

        return _slime.TryEat((owner, slime), target) ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed;
    }
}
