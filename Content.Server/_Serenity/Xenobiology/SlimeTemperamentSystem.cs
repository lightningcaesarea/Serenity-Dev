using Content.Server.Popups;
using Content.Shared._Serenity.Xenobiology;
using Content.Shared._Starlight.Xenobiology;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Server._Serenity.Xenobiology;

/// <summary>
/// Decides when a slime is desperate enough to feed on crew, and what it may feed on.
/// See <see cref="SlimeTemperamentComponent"/>.
/// </summary>
public sealed partial class SlimeTemperamentSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedItemSystem _item = default!;

    private static readonly ProtoId<DamageContainerPrototype> Biological = "Biological";
    private static readonly ProtoId<ItemSizePrototype> DocileSize = "Large";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SlimeDocilityPotionComponent, AfterInteractEvent>(OnDocilityPotion);
    }

    private void OnDocilityPotion(Entity<SlimeDocilityPotionComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target || !args.CanReach)
            return;

        if (!TryComp<SlimeTemperamentComponent>(target, out var temperament))
            return;

        args.Handled = true;
        var name = Identity(target);
        if (temperament.Docile)
        {
            _popup.PopupEntity(Loc.GetString("slime-docility-potion-already", ("slime", name)), args.User, args.User);
            return;
        }

        MakeDocile((target, temperament));
        _popup.PopupEntity(Loc.GetString("slime-docility-potion-applied", ("slime", name)), args.User, args.User);
        QueueDel(ent);
    }

    /// <summary>
    /// Makes a slime docile for good: it never turns desperate, and it can be picked up and carried.
    /// </summary>
    public void MakeDocile(Entity<SlimeTemperamentComponent> slime)
    {
        slime.Comp.Docile = true;
        slime.Comp.Desperate = false;
        Dirty(slime);

        var item = EnsureComp<ItemComponent>(slime);
        _item.SetSize(slime, DocileSize, item);
    }

    /// <summary>
    /// Whether this slime is desperate and will feed on crew: it starved out and hasn't yet eaten its way back
    /// up to <see cref="SlimeTemperamentComponent.CalmAt"/>.
    /// </summary>
    public bool IsDesperate(EntityUid slime, SlimeTemperamentComponent? temperament = null, HungerComponent? hunger = null)
    {
        if (!Resolve(slime, ref temperament, false) || !Resolve(slime, ref hunger, false))
            return false;

        if (temperament.Docile)
            return temperament.Desperate = false;

        var threshold = _hunger.GetHungerThreshold(hunger);
        if (threshold <= temperament.DesperateAt)
            temperament.Desperate = true;
        else if (threshold >= temperament.CalmAt)
            temperament.Desperate = false;

        return temperament.Desperate;
    }

    /// <summary>
    /// Whether a desperate slime may feed on this creature: a living organic mob that isn't a slime and hasn't
    /// already taken <see cref="SlimeTemperamentComponent.DamageCap"/> damage.
    /// </summary>
    public bool IsDesperateTarget(SlimeTemperamentComponent temperament, EntityUid target)
    {
        if (HasComp<SlimeComponent>(target))
            return false;

        if (!TryComp<DamageableComponent>(target, out var damageable) || damageable.DamageContainerID != Biological)
            return false;

        if (!_mobState.IsAlive(target))
            return false;

        return _damageable.GetTotalDamage(target) < temperament.DamageCap;
    }

    /// <summary>
    /// The slime's mood for the scanner readout.
    /// </summary>
    public string GetMoodText(EntityUid slime)
    {
        if (!TryComp<SlimeTemperamentComponent>(slime, out var temperament))
            return Loc.GetString("slime-mood-calm");

        if (temperament.Docile)
            return Loc.GetString("slime-mood-docile");

        if (IsDesperate(slime, temperament))
            return Loc.GetString("slime-mood-desperate");

        if (TryComp<HungerComponent>(slime, out var hunger) && _hunger.GetHungerThreshold(hunger) <= HungerThreshold.Starving)
            return Loc.GetString("slime-mood-hungry");

        return Loc.GetString("slime-mood-calm");
    }

    private string Identity(EntityUid uid) => MetaData(uid).EntityName;
}
