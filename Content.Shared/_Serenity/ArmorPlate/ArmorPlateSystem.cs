using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Serenity.ArmorPlate;

/// <summary>
/// Armor plates: a plate slotted into a carrier's storage absorbs part of each hit the wearer takes,
/// loses durability for it, can slow the wearer down and can cost them stamina.
/// </summary>
/// <remarks>
/// Serenity reimplementation of the armor plate design from Monolith (Monolith-Station/Monolith).
/// </remarks>
public sealed partial class ArmorPlateSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;

    private static readonly ProtoId<DamageTypePrototype> PlateWearType = "Blunt";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArmorPlateCarrierComponent, EntInsertedIntoContainerMessage>(OnCarrierInserted);
        SubscribeLocalEvent<ArmorPlateCarrierComponent, EntRemovedFromContainerMessage>(OnCarrierRemoved);
        SubscribeLocalEvent<ArmorPlateCarrierComponent, GotEquippedEvent>(OnCarrierEquipped);
        SubscribeLocalEvent<ArmorPlateCarrierComponent, GotUnequippedEvent>(OnCarrierUnequipped);
        SubscribeLocalEvent<ArmorPlateCarrierComponent, InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnCarrierRefreshSpeed);
        SubscribeLocalEvent<ArmorPlateCarrierComponent, ExaminedEvent>(OnCarrierExamined);

        SubscribeLocalEvent<ArmorPlateComponent, ExaminedEvent>(OnPlateExamined);
        SubscribeLocalEvent<ArmorPlateComponent, GetVerbsEvent<ExamineVerb>>(OnPlateExamineVerbs);
        SubscribeLocalEvent<ArmorPlateComponent, EntityTerminatingEvent>(OnPlateTerminating);

        SubscribeLocalEvent<ArmorPlateWearerComponent, BeforeDamageChangedEvent>(OnWearerBeforeDamage);
    }

    #region Carrier

    private void OnCarrierInserted(Entity<ArmorPlateCarrierComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != StorageComponent.ContainerId || ent.Comp.ActivePlate != null)
            return;

        if (HasComp<ArmorPlateComponent>(args.Entity))
            SetActivePlate(ent, args.Entity);
    }

    private void OnCarrierRemoved(Entity<ArmorPlateCarrierComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != StorageComponent.ContainerId || ent.Comp.ActivePlate != args.Entity)
            return;

        // Fall back to the next plate still in the storage, if any.
        EntityUid? next = null;
        foreach (var contained in args.Container.ContainedEntities)
        {
            if (contained == args.Entity || !HasComp<ArmorPlateComponent>(contained))
                continue;

            next = contained;
            break;
        }

        SetActivePlate(ent, next);
    }

    private void SetActivePlate(Entity<ArmorPlateCarrierComponent> ent, EntityUid? plate)
    {
        ent.Comp.ActivePlate = plate;
        Dirty(ent);

        if (!TryGetWearer(ent, out var wearer))
            return;

        _movementSpeed.RefreshMovementSpeedModifiers(wearer.Value);
        RefreshWearer(wearer.Value);
    }

    private void OnCarrierEquipped(Entity<ArmorPlateCarrierComponent> ent, ref GotEquippedEvent args)
    {
        RefreshWearer(args.EquipTarget);
        _movementSpeed.RefreshMovementSpeedModifiers(args.EquipTarget);
    }

    private void OnCarrierUnequipped(Entity<ArmorPlateCarrierComponent> ent, ref GotUnequippedEvent args)
    {
        RefreshWearer(args.EquipTarget);
        _movementSpeed.RefreshMovementSpeedModifiers(args.EquipTarget);
    }

    private void OnCarrierRefreshSpeed(Entity<ArmorPlateCarrierComponent> ent, ref InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        if (TryGetActivePlate(ent, out var plate))
            args.Args.ModifySpeed(plate.Comp.WalkSpeedModifier, plate.Comp.SprintSpeedModifier);
    }

    private void OnCarrierExamined(Entity<ArmorPlateCarrierComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!TryGetActivePlate(ent, out var plate))
        {
            args.PushMarkup(Loc.GetString("serenity-armor-plate-carrier-empty"));
            return;
        }

        var name = Name(plate);
        if (TryGetDurabilityPercent(plate, out var percent))
        {
            args.PushMarkup(Loc.GetString("serenity-armor-plate-carrier-plate-durability",
                ("plate", name),
                ("percent", percent),
                ("color", DurabilityColor(percent))));
        }
        else
        {
            args.PushMarkup(Loc.GetString("serenity-armor-plate-carrier-plate", ("plate", name)));
        }
    }

    /// <summary>
    /// Gets the mob wearing this clothing, if it is in one of their inventory slots.
    /// </summary>
    private bool TryGetWearer(EntityUid clothing, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EntityUid? wearer)
    {
        wearer = null;
        if (!_container.TryGetContainingContainer(clothing, out var container)
            || !_inventory.TryGetSlot(container.Owner, container.ID, out _))
            return false;

        wearer = container.Owner;
        return true;
    }

    /// <summary>
    /// Adds or removes the wearer marker depending on whether anything they wear has an active plate.
    /// </summary>
    private void RefreshWearer(EntityUid wearer)
    {
        if (TerminatingOrDeleted(wearer))
            return;

        var enumerator = _inventory.GetSlotEnumerator(wearer);
        while (enumerator.NextItem(out var item))
        {
            if (TryComp<ArmorPlateCarrierComponent>(item, out var carrier) && TryGetActivePlate((item, carrier), out _))
            {
                EnsureComp<ArmorPlateWearerComponent>(wearer);
                return;
            }
        }

        RemCompDeferred<ArmorPlateWearerComponent>(wearer);
    }

    public bool TryGetActivePlate(Entity<ArmorPlateCarrierComponent> carrier, out Entity<ArmorPlateComponent> plate)
    {
        plate = default;
        if (carrier.Comp.ActivePlate is not { } uid
            || TerminatingOrDeleted(uid)
            || !TryComp<ArmorPlateComponent>(uid, out var comp))
            return false;

        plate = (uid, comp);
        return true;
    }

    #endregion

    #region Damage

    private void OnWearerBeforeDamage(Entity<ArmorPlateWearerComponent> ent, ref BeforeDamageChangedEvent args)
    {
        // Only hits from something (weapons, projectiles, thrown items) touch the plate, not bleeding,
        // suffocation, healing or other damage without an origin.
        if (args.Cancelled || args.Origin == null || !args.Damage.AnyPositive())
            return;

        var enumerator = _inventory.GetSlotEnumerator(ent.Owner);
        while (enumerator.NextItem(out var item))
        {
            if (!TryComp<ArmorPlateCarrierComponent>(item, out var carrier)
                || !TryGetActivePlate((item, carrier), out var plate))
                continue;

            ApplyPlate(plate, args.Damage, out var absorbed, out var amplified, out var incoming, out var wear);

            if (plate.Comp.Durability != null && wear > FixedPoint2.Zero)
            {
                var wearDamage = new DamageSpecifier();
                wearDamage.DamageDict.Add(PlateWearType, wear);
                _damageable.TryChangeDamage(plate.Owner, wearDamage, ignoreResistances: true, interruptsDoAfters: false);
            }

            var stamina = plate.Comp.StaminaPerHit > 0f
                ? incoming.Float() * plate.Comp.StaminaPerHit
                : absorbed.Float() * plate.Comp.StaminaPerAbsorbed + amplified.Float() * plate.Comp.StaminaPerAmplified;

            if (stamina > 0f)
                _stamina.TakeStaminaDamage(ent.Owner, stamina);

            if (args.Damage.Empty)
            {
                args.Cancelled = true;
                return;
            }
        }
    }

    /// <summary>
    /// Rewrites <paramref name="damage"/> in place with what gets past the plate, the same way other
    /// <see cref="BeforeDamageChangedEvent"/> handlers change incoming damage.
    /// </summary>
    private static void ApplyPlate(
        Entity<ArmorPlateComponent> plate,
        DamageSpecifier damage,
        out FixedPoint2 absorbed,
        out FixedPoint2 amplified,
        out FixedPoint2 incoming,
        out FixedPoint2 wear)
    {
        absorbed = FixedPoint2.Zero;
        amplified = FixedPoint2.Zero;
        incoming = FixedPoint2.Zero;
        wear = FixedPoint2.Zero;

        foreach (var type in new List<ProtoId<DamageTypePrototype>>(damage.DamageDict.Keys))
        {
            var amount = damage.DamageDict[type];
            if (amount <= FixedPoint2.Zero)
                continue;

            incoming += amount;
            wear += amount * plate.Comp.Wear.GetValueOrDefault(type, 0f);

            var ratio = plate.Comp.Absorption.GetValueOrDefault(type, 0f);
            if (ratio == 0f)
                continue;

            var stopped = amount * Math.Clamp(ratio, -10f, 1f);
            var remaining = amount - stopped;

            if (ratio > 0f)
                absorbed += stopped;
            else
                amplified += remaining - amount;

            if (remaining <= FixedPoint2.Zero)
                damage.DamageDict.Remove(type);
            else
                damage.DamageDict[type] = remaining;
        }
    }

    private void OnPlateTerminating(Entity<ArmorPlateComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!_container.TryGetContainingContainer(ent.Owner, out var container)
            || !TryComp<ArmorPlateCarrierComponent>(container.Owner, out var carrier)
            || carrier.ActivePlate != ent.Owner
            || !carrier.BreakPopup
            || !TryGetWearer(container.Owner, out var wearer))
            return;

        _popup.PopupEntity(Loc.GetString("serenity-armor-plate-broke", ("plate", Name(ent))),
            wearer.Value, wearer.Value, PopupType.MediumCaution);
    }

    #endregion

    #region Examine

    private bool TryGetDurabilityPercent(Entity<ArmorPlateComponent> plate, out int percent)
    {
        percent = 100;
        if (plate.Comp.Durability is not { } durability || durability <= 0)
            return false;

        var damage = _damageable.GetTotalDamage(plate.Owner).Float();
        percent = (int) Math.Clamp((durability - damage) / durability * 100f, 0f, 100f);
        return true;
    }

    private static string DurabilityColor(int percent)
    {
        return percent switch
        {
            > 66 => "green",
            >= 33 => "yellow",
            _ => "red",
        };
    }

    private void OnPlateExamined(Entity<ArmorPlateComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !TryGetDurabilityPercent(ent, out var percent))
            return;

        args.PushMarkup(Loc.GetString("serenity-armor-plate-durability",
            ("percent", percent),
            ("color", DurabilityColor(percent))));
    }

    private void OnPlateExamineVerbs(Entity<ArmorPlateComponent> ent, ref GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var plate = ent.Comp;
        var msg = new FormattedMessage();
        msg.AddMarkupOrThrow(Loc.GetString("serenity-armor-plate-stats-header"));

        if (plate.Durability != null)
        {
            msg.PushNewline();
            msg.AddMarkupOrThrow(Loc.GetString("serenity-armor-plate-stats-durability", ("durability", plate.Durability.Value)));
        }

        foreach (var (type, ratio) in plate.Absorption)
        {
            if (ratio == 0f)
                continue;

            msg.PushNewline();
            msg.AddMarkupOrThrow(Loc.GetString(ratio > 0f ? "serenity-armor-plate-stats-absorbs" : "serenity-armor-plate-stats-amplifies",
                ("type", Loc.GetString("armor-damage-type-" + type.Id.ToLowerInvariant())),
                ("percent", MathF.Round(MathF.Abs(ratio) * 100f, 1))));
        }

        AddSpeedLine(msg, "serenity-armor-plate-stats-walk", plate.WalkSpeedModifier);
        AddSpeedLine(msg, "serenity-armor-plate-stats-sprint", plate.SprintSpeedModifier);

        if (plate.StaminaPerHit > 0f)
        {
            msg.PushNewline();
            msg.AddMarkupOrThrow(Loc.GetString("serenity-armor-plate-stats-stamina-hit", ("percent", MathF.Round(plate.StaminaPerHit * 100f, 1))));
        }
        else
        {
            if (plate.StaminaPerAbsorbed > 0f)
            {
                msg.PushNewline();
                msg.AddMarkupOrThrow(Loc.GetString("serenity-armor-plate-stats-stamina-absorbed", ("percent", MathF.Round(plate.StaminaPerAbsorbed * 100f, 1))));
            }

            if (plate.StaminaPerAmplified > 0f)
            {
                msg.PushNewline();
                msg.AddMarkupOrThrow(Loc.GetString("serenity-armor-plate-stats-stamina-amplified", ("percent", MathF.Round(plate.StaminaPerAmplified * 100f, 1))));
            }
        }

        _examine.AddDetailedExamineVerb(args, plate, msg,
            Loc.GetString("serenity-armor-plate-verb-text"),
            "/Textures/Interface/VerbIcons/dot.svg.192dpi.png",
            Loc.GetString("serenity-armor-plate-verb-message"));
    }

    private void AddSpeedLine(FormattedMessage msg, string locId, float modifier)
    {
        var percent = MathF.Round((modifier - 1f) * 100f, 1);
        if (percent == 0f)
            return;

        msg.PushNewline();
        msg.AddMarkupOrThrow(Loc.GetString(locId, ("change", percent > 0f ? "faster" : "slower"), ("percent", MathF.Abs(percent))));
    }

    #endregion
}
