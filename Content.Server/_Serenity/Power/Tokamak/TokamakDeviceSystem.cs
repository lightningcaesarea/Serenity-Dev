using Content.Server.Stack;
using Content.Shared._Serenity.Power.Tokamak;
using Content.Shared.Audio;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._Serenity.Power.Tokamak;

/// <summary>
/// Gyrotrons, fuel injectors, fuel rods and harvesters of the tokamak.
/// </summary>
public sealed class TokamakDeviceSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedPowerReceiverSystem _receiver = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StackSystem _stack = default!;

    private static readonly TimeSpan TickLength = TimeSpan.FromSeconds(1);

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TokamakGyrotronComponent, ActivateInWorldEvent>(OnGyrotronActivate);
        SubscribeLocalEvent<TokamakGyrotronComponent, ExaminedEvent>(OnGyrotronExamined);
        SubscribeLocalEvent<TokamakGyrotronComponent, MapInitEvent>(OnGyrotronInit);

        SubscribeLocalEvent<TokamakFuelInjectorComponent, ActivateInWorldEvent>(OnInjectorActivate);
        SubscribeLocalEvent<TokamakFuelInjectorComponent, ExaminedEvent>(OnInjectorExamined);
        SubscribeLocalEvent<TokamakFuelInjectorComponent, MapInitEvent>(OnInjectorInit);
        SubscribeLocalEvent<TokamakFuelInjectorComponent, EntInsertedIntoContainerMessage>(OnRodChanged);
        SubscribeLocalEvent<TokamakFuelInjectorComponent, EntRemovedFromContainerMessage>(OnRodChanged);

        SubscribeLocalEvent<TokamakFuelRodComponent, ExaminedEvent>(OnRodExamined);

        SubscribeLocalEvent<TokamakHarvesterComponent, ActivateInWorldEvent>(OnHarvesterActivate);
        SubscribeLocalEvent<TokamakHarvesterComponent, ExaminedEvent>(OnHarvesterExamined);
        SubscribeLocalEvent<TokamakHarvesterComponent, MapInitEvent>(OnHarvesterInit);
    }

    #region Gyrotron

    private void OnGyrotronInit(Entity<TokamakGyrotronComponent> ent, ref MapInitEvent args)
    {
        UpdateGyrotron(ent);
    }

    private void OnGyrotronActivate(Entity<TokamakGyrotronComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        SetGyrotronActive(ent, !ent.Comp.Active, args.User);
    }

    public void SetGyrotronActive(Entity<TokamakGyrotronComponent> ent, bool active, EntityUid? user = null)
    {
        if (ent.Comp.Active == active)
            return;

        if (active && !_receiver.IsPowered(ent.Owner))
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("tokamak-device-no-power"), ent.Owner, user.Value);

            return;
        }

        ent.Comp.Active = active;
        _audio.PlayPvs(ent.Comp.ActivateSound, ent.Owner);
        UpdateGyrotron(ent);
    }

    public void SetGyrotronSettings(Entity<TokamakGyrotronComponent> ent, float? rate, float? megaEnergy)
    {
        if (rate != null)
            ent.Comp.Rate = Math.Clamp(rate.Value, 0.5f, ent.Comp.MaxRate);

        if (megaEnergy != null)
            ent.Comp.MegaEnergy = Math.Clamp(megaEnergy.Value, 1f, ent.Comp.MaxMegaEnergy);

        UpdateGyrotron(ent);
    }

    private void UpdateGyrotron(Entity<TokamakGyrotronComponent> ent)
    {
        var comp = ent.Comp;
        var draw = comp.Active ? comp.IdleDraw + comp.Rate * comp.MegaEnergy * comp.DrawPerUnit : 0f;
        _receiver.SetLoad(ent.Owner, draw);
        _ambient.SetAmbience(ent.Owner, comp.Active);
        _appearance.SetData(ent.Owner, TokamakVisuals.DeviceState, comp.Active ? TokamakDeviceState.Active : TokamakDeviceState.Off);
    }

    private void OnGyrotronExamined(Entity<TokamakGyrotronComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("tokamak-gyrotron-examine",
            ("state", ent.Comp.Active ? "on" : "off"),
            ("rate", ent.Comp.Rate),
            ("energy", ent.Comp.MegaEnergy)));
    }

    #endregion

    #region Injector

    private void OnInjectorInit(Entity<TokamakFuelInjectorComponent> ent, ref MapInitEvent args)
    {
        UpdateInjector(ent);
    }

    private void OnInjectorActivate(Entity<TokamakFuelInjectorComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        SetInjectorActive(ent, !ent.Comp.Active, args.User);
    }

    public void SetInjectorActive(Entity<TokamakFuelInjectorComponent> ent, bool active, EntityUid? user = null)
    {
        if (ent.Comp.Active == active)
            return;

        if (active && !_receiver.IsPowered(ent.Owner))
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("tokamak-device-no-power"), ent.Owner, user.Value);

            return;
        }

        ent.Comp.Active = active;
        _audio.PlayPvs(ent.Comp.ActivateSound, ent.Owner);
        UpdateInjector(ent);
    }

    public void SetInjectorRate(Entity<TokamakFuelInjectorComponent> ent, float rate)
    {
        ent.Comp.Rate = Math.Clamp(rate, 0.5f, ent.Comp.MaxRate);
        UpdateInjector(ent);
    }

    private void OnRodChanged(Entity<TokamakFuelInjectorComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        UpdateInjector(ent);
    }

    private void OnRodChanged(Entity<TokamakFuelInjectorComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        UpdateInjector(ent);
    }

    private void UpdateInjector(Entity<TokamakFuelInjectorComponent> ent)
    {
        var comp = ent.Comp;
        var draw = comp.Active ? comp.IdleDraw + comp.Rate * comp.DrawPerUnit : 0f;
        _receiver.SetLoad(ent.Owner, draw);

        var hasRod = _slots.GetItemOrNull(ent.Owner, comp.RodSlotId) != null;
        var state = !hasRod
            ? TokamakDeviceState.Off
            : comp.Active
                ? TokamakDeviceState.Active
                : TokamakDeviceState.Idle;

        _appearance.SetData(ent.Owner, TokamakVisuals.DeviceState, state);
    }

    private void OnInjectorExamined(Entity<TokamakFuelInjectorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("tokamak-injector-examine",
            ("state", ent.Comp.Active ? "on" : "off"),
            ("rate", ent.Comp.Rate)));
    }

    #endregion

    #region Fuel rods

    private void OnRodExamined(Entity<TokamakFuelRodComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var any = false;
        foreach (var (reactant, amount) in ent.Comp.Reactants)
        {
            if (amount < 0.1f)
                continue;

            any = true;
            args.PushMarkup(Loc.GetString("tokamak-rod-examine-entry",
                ("reactant", Loc.GetString($"tokamak-reactant-{reactant}")),
                ("amount", MathF.Round(amount))));
        }

        if (!any)
            args.PushMarkup(Loc.GetString("tokamak-rod-examine-empty"));
    }

    #endregion

    #region Harvester

    private void OnHarvesterInit(Entity<TokamakHarvesterComponent> ent, ref MapInitEvent args)
    {
        UpdateHarvester(ent);
    }

    private void OnHarvesterActivate(Entity<TokamakHarvesterComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        SetHarvesterActive(ent, !ent.Comp.Active, args.User);
    }

    public void SetHarvesterActive(Entity<TokamakHarvesterComponent> ent, bool active, EntityUid? user = null)
    {
        if (ent.Comp.Active == active)
            return;

        if (active && !_receiver.IsPowered(ent.Owner))
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("tokamak-device-no-power"), ent.Owner, user.Value);

            return;
        }

        ent.Comp.Active = active;
        _audio.PlayPvs(ent.Comp.ActivateSound, ent.Owner);
        UpdateHarvester(ent);
    }

    private void UpdateHarvester(Entity<TokamakHarvesterComponent> ent)
    {
        _receiver.SetLoad(ent.Owner, ent.Comp.Active ? ent.Comp.IdleDraw : 0f);
        _ambient.SetAmbience(ent.Owner, ent.Comp.Active);
        _appearance.SetData(ent.Owner, TokamakVisuals.DeviceState, ent.Comp.Active ? TokamakDeviceState.Active : TokamakDeviceState.Off);
    }

    private void OnHarvesterExamined(Entity<TokamakHarvesterComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("tokamak-harvester-examine", ("state", ent.Comp.Active ? "on" : "off")));
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + TickLength;

        var query = EntityQueryEnumerator<TokamakHarvesterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var harvester, out var xform))
        {
            if (!harvester.Active)
                continue;

            if (!_receiver.IsPowered(uid))
            {
                harvester.Active = false;
                UpdateHarvester((uid, harvester));
                continue;
            }

            Harvest((uid, harvester), xform);
        }
    }

    private void Harvest(Entity<TokamakHarvesterComponent> ent, TransformComponent xform)
    {
        var pos = _transform.GetWorldPosition(xform);
        TokamakCoreComponent? nearest = null;
        var best = ent.Comp.Range * ent.Comp.Range;

        var cores = EntityQueryEnumerator<TokamakCoreComponent, TransformComponent>();
        while (cores.MoveNext(out _, out var core, out var coreXform))
        {
            if (!core.Active || coreXform.MapID != xform.MapID)
                continue;

            var distance = (_transform.GetWorldPosition(coreXform) - pos).LengthSquared();
            if (distance > best)
                continue;

            best = distance;
            nearest = core;
        }

        if (nearest == null)
            return;

        foreach (var (reactant, stack) in ent.Comp.Outputs)
        {
            if (!nearest.Reactants.TryGetValue(reactant, out var available) || available <= 0f)
                continue;

            var pulled = MathF.Min(available, ent.Comp.PullRate);
            nearest.Reactants[reactant] = available - pulled;

            var buffered = ent.Comp.Buffer.GetValueOrDefault(reactant) + pulled;
            var sheets = (int) (buffered / ent.Comp.UnitsPerSheet);
            ent.Comp.Buffer[reactant] = buffered - sheets * ent.Comp.UnitsPerSheet;

            if (sheets > 0)
                _stack.SpawnMultipleAtPosition(stack, sheets, xform.Coordinates);
        }
    }

    #endregion
}
