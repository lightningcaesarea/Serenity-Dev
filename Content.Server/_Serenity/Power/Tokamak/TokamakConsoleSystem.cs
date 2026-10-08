using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Shared._Serenity.Power.Tokamak;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.Power.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._Serenity.Power.Tokamak;

/// <summary>
/// Drives the tokamak console: finds the nearest core, sends its state to the UI and applies the controls.
/// </summary>
public sealed class TokamakConsoleSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedPowerReceiverSystem _receiver = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TokamakCoreSystem _core = default!;
    [Dependency] private TokamakDeviceSystem _devices = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    private static readonly TimeSpan UiInterval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _nextUiUpdate;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TokamakConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<TokamakConsoleComponent, TokamakSetActiveMessage>(OnSetActive);
        SubscribeLocalEvent<TokamakConsoleComponent, TokamakSetFieldStrengthMessage>(OnSetFieldStrength);
        SubscribeLocalEvent<TokamakConsoleComponent, TokamakScramMessage>(OnScram);
        SubscribeLocalEvent<TokamakConsoleComponent, TokamakDeviceSettingMessage>(OnDeviceSetting);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUiUpdate)
            return;

        _nextUiUpdate = _timing.CurTime + UiInterval;

        var query = EntityQueryEnumerator<TokamakConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (!_ui.IsUiOpen(uid, TokamakConsoleUiKey.Key))
                continue;

            UpdateUi((uid, console));
        }
    }

    private void OnOpened(Entity<TokamakConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    #region Messages

    private void OnSetActive(Entity<TokamakConsoleComponent> ent, ref TokamakSetActiveMessage args)
    {
        if (TryFindCore(ent, out var core))
            _core.SetActive(core.Value, args.Active, args.Actor);

        UpdateUi(ent);
    }

    private void OnSetFieldStrength(Entity<TokamakConsoleComponent> ent, ref TokamakSetFieldStrengthMessage args)
    {
        if (TryFindCore(ent, out var core))
            _core.SetFieldStrength(core.Value, args.FieldStrength, args.Actor);

        UpdateUi(ent);
    }

    private void OnScram(Entity<TokamakConsoleComponent> ent, ref TokamakScramMessage args)
    {
        if (TryFindCore(ent, out var core))
        {
            _adminLog.Add(LogType.Action, LogImpact.High,
                $"{ToPrettyString(args.Actor):user} scrammed the tokamak {ToPrettyString(core.Value.Owner):core} from {ToPrettyString(ent.Owner):console}");
            _core.Scram(core.Value, args.Actor);
        }

        UpdateUi(ent);
    }

    private void OnDeviceSetting(Entity<TokamakConsoleComponent> ent, ref TokamakDeviceSettingMessage args)
    {
        if (!TryFindCore(ent, out var core))
            return;

        var device = GetEntity(args.Device);
        if (!Exists(device) || !InRangeOfCore(core.Value, device, ent.Comp.Range))
            return;

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Actor):user} set {args.Setting} of {ToPrettyString(device):device} to {args.Value} from {ToPrettyString(ent.Owner):console}");

        if (TryComp<TokamakGyrotronComponent>(device, out var gyrotron))
        {
            var gyro = (device, gyrotron);
            switch (args.Setting)
            {
                case TokamakDeviceSetting.Active:
                    _devices.SetGyrotronActive(gyro, args.Value > 0.5f, args.Actor);
                    break;
                case TokamakDeviceSetting.Rate:
                    _devices.SetGyrotronSettings(gyro, args.Value, null);
                    break;
                case TokamakDeviceSetting.MegaEnergy:
                    _devices.SetGyrotronSettings(gyro, null, args.Value);
                    break;
            }
        }
        else if (TryComp<TokamakFuelInjectorComponent>(device, out var injector))
        {
            var inj = (device, injector);
            switch (args.Setting)
            {
                case TokamakDeviceSetting.Active:
                    _devices.SetInjectorActive(inj, args.Value > 0.5f, args.Actor);
                    break;
                case TokamakDeviceSetting.Rate:
                    _devices.SetInjectorRate(inj, args.Value);
                    break;
            }
        }
        else if (TryComp<TokamakHarvesterComponent>(device, out var harvester))
        {
            if (args.Setting == TokamakDeviceSetting.Active)
                _devices.SetHarvesterActive((device, harvester), args.Value > 0.5f, args.Actor);
        }

        UpdateUi(ent);
    }

    #endregion

    #region State

    private bool TryFindCore(Entity<TokamakConsoleComponent> console, [NotNullWhen(true)] out Entity<TokamakCoreComponent>? found)
    {
        found = null;
        var xform = Transform(console.Owner);
        var pos = _transform.GetWorldPosition(xform);
        var best = console.Comp.Range * console.Comp.Range;

        var query = EntityQueryEnumerator<TokamakCoreComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var core, out var coreXform))
        {
            if (coreXform.MapID != xform.MapID)
                continue;

            var distance = (_transform.GetWorldPosition(coreXform) - pos).LengthSquared();
            if (distance > best)
                continue;

            best = distance;
            found = (uid, core);
        }

        return found != null;
    }

    private bool InRangeOfCore(Entity<TokamakCoreComponent> core, EntityUid device, float range)
    {
        var coreXform = Transform(core.Owner);
        var deviceXform = Transform(device);
        if (coreXform.MapID != deviceXform.MapID)
            return false;

        var distance = (_transform.GetWorldPosition(coreXform) - _transform.GetWorldPosition(deviceXform)).LengthSquared();
        return distance <= range * range;
    }

    private void UpdateUi(Entity<TokamakConsoleComponent> ent)
    {
        var state = new TokamakConsoleBuiState();

        if (TryFindCore(ent, out var found))
        {
            var core = found.Value.Comp;
            state.HasCore = true;
            state.Active = core.Active;
            state.Powered = _receiver.IsPowered(found.Value.Owner);
            state.FieldStrength = core.FieldStrength;
            state.MinFieldStrength = core.MinFieldStrength;
            state.MaxFieldStrength = core.MaxFieldStrength;
            state.PlasmaTemperature = core.PlasmaTemperature;
            state.Instability = core.Instability;
            state.RuptureInstability = core.RuptureInstability;
            state.PowerOutput = core.PowerOutput;
            state.PowerDraw = core.PowerDraw;
            state.RadiationLevel = core.RadiationLevel;
            state.MaxReactants = core.MaxReactants;

            foreach (var (reactant, amount) in core.Reactants.OrderByDescending(r => r.Value))
            {
                state.TotalReactants += amount;
                state.Reactants.Add(new TokamakReactantEntry(reactant, amount));
            }

            AddDevices(state, found.Value, ent.Comp.Range);
        }

        _ui.SetUiState(ent.Owner, TokamakConsoleUiKey.Key, state);
    }

    private void AddDevices(TokamakConsoleBuiState state, Entity<TokamakCoreComponent> core, float range)
    {
        var corePos = _transform.GetWorldPosition(Transform(core.Owner));

        var gyrotrons = EntityQueryEnumerator<TokamakGyrotronComponent, TransformComponent>();
        while (gyrotrons.MoveNext(out var uid, out var gyro, out var xform))
        {
            if (!InRangeOfCore(core, uid, range))
                continue;

            var aligned = TokamakBeam.IsAligned(_transform.GetWorldPosition(xform), _transform.GetWorldRotation(xform), corePos, gyro.Range);
            state.Devices.Add(new TokamakDeviceEntry(GetNetEntity(uid), TokamakDeviceKind.Gyrotron, Name(uid),
                gyro.Active, aligned, gyro.Rate, gyro.MaxRate, gyro.MegaEnergy, gyro.MaxMegaEnergy, 0f));
        }

        var injectors = EntityQueryEnumerator<TokamakFuelInjectorComponent, TransformComponent>();
        while (injectors.MoveNext(out var uid, out var injector, out var xform))
        {
            if (!InRangeOfCore(core, uid, range))
                continue;

            var aligned = TokamakBeam.IsAligned(_transform.GetWorldPosition(xform), _transform.GetWorldRotation(xform), corePos, injector.Range);
            var fill = 0f;
            var rodUid = _slots.GetItemOrNull(uid, injector.RodSlotId);
            if (rodUid != null && TryComp<TokamakFuelRodComponent>(rodUid, out var rod))
                fill = MathF.Min(1f, rod.Reactants.Values.Sum() / MathF.Max(1f, rod.Capacity));

            state.Devices.Add(new TokamakDeviceEntry(GetNetEntity(uid), TokamakDeviceKind.Injector, Name(uid),
                injector.Active, aligned, injector.Rate, injector.MaxRate, 0f, 0f, fill));
        }

        var harvesters = EntityQueryEnumerator<TokamakHarvesterComponent>();
        while (harvesters.MoveNext(out var uid, out var harvester))
        {
            if (!InRangeOfCore(core, uid, range))
                continue;

            state.Devices.Add(new TokamakDeviceEntry(GetNetEntity(uid), TokamakDeviceKind.Harvester, Name(uid),
                harvester.Active, true, 0f, 0f, 0f, 0f, 0f));
        }
    }

    #endregion
}
