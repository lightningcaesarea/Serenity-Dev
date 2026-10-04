using Content.Shared.FixedPoint;
using Content.Shared.Nutrition.Components;
using Robust.Shared.GameStates;

namespace Content.Shared._Serenity.Xenobiology;

/// <summary>
/// How dangerous a slime is. A fed slime is calm and only eats monkeys. A slime that starves out with no
/// monkey in reach turns desperate and feeds on any living organic creature nearby, crew included, until it
/// has eaten again. A docile slime never turns desperate and can be picked up.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SlimeTemperamentComponent : Component
{
    /// <summary>
    /// Set by the docility potion. A docile slime never turns desperate. Not passed on when it splits.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Docile;

    /// <summary>
    /// The slime turns desperate once its hunger is at or below this threshold.
    /// Freshly split slimes start out starving, so this sits below that to give the lab time to feed them.
    /// </summary>
    [DataField]
    public HungerThreshold DesperateAt = HungerThreshold.Dead;

    /// <summary>
    /// A desperate slime stays desperate until its hunger is back at or above this threshold,
    /// so it keeps feeding for several bites instead of calming down after one.
    /// </summary>
    [DataField]
    public HungerThreshold CalmAt = HungerThreshold.Peckish;

    /// <summary>
    /// Whether the slime is currently desperate. Updated by the slime temperament system as hunger changes.
    /// </summary>
    [ViewVariables]
    public bool Desperate;

    /// <summary>
    /// A desperate slime stops feeding on a creature once its total damage reaches this,
    /// so a hungry slime injures people rather than putting them into crit.
    /// </summary>
    [DataField]
    public FixedPoint2 DamageCap = 50;

    /// <summary>
    /// How far a desperate slime looks for something to feed on.
    /// </summary>
    [DataField]
    public float SearchRange = 5f;
}
