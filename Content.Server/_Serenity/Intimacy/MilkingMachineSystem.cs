using Content.Shared._Serenity.Consent;
using Content.Shared._Serenity.Intimacy;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._Serenity.Intimacy;

/// <summary>
/// Runs <see cref="MilkingMachineComponent"/>: raises the occupant's arousal and pleasure, collects fluids into
/// the machine's tank every second, and adds a burst when the occupant climaxes. Plumbing pulls from the tank.
/// Also serves the control window, which shows the occupant, the pump mode and the tank.
/// </summary>
public sealed partial class MilkingMachineSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SharedConsentSystem _consent = default!;
    [Dependency] private SharedIntimacySystem _intimacy = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MilkingMachineComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MilkingMachineComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<MilkingMachineComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<MilkingMachineComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<MilkingMachineComponent, BoundUIOpenedEvent>(OnUIOpened);
        SubscribeLocalEvent<MilkingMachineComponent, MilkingMachineSetModeMessage>(OnSetModeMessage);
        SubscribeLocalEvent<BuckleComponent, ClimaxEvent>(OnClimax);
    }

    private void OnMapInit(Entity<MilkingMachineComponent> ent, ref MapInitEvent args)
    {
        UpdateAppearance(ent);
    }

    /// <summary>
    /// The one occupant the machine works on, if anyone is buckled in.
    /// </summary>
    public EntityUid? GetOccupant(EntityUid machine)
    {
        if (!TryComp<StrapComponent>(machine, out var strap))
            return null;

        foreach (var buckled in strap.BuckledEntities)
            return buckled;

        return null;
    }

    private void OnGetVerbs(Entity<MilkingMachineComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        foreach (var mode in Enum.GetValues<MilkingMachineMode>())
        {
            if (mode == ent.Comp.Mode)
                continue;

            var target = mode;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString($"milking-machine-mode-{mode.ToString().ToLowerInvariant()}"),
                Category = VerbCategory.SelectType,
                Priority = -(int) mode,
                Act = () => TrySetMode(ent, target, user),
            });
        }
    }

    /// <summary>
    /// Switches the machine's strength. Anyone may turn it off; turning it on for someone else needs their consent.
    /// </summary>
    public bool TrySetMode(Entity<MilkingMachineComponent> ent, MilkingMachineMode mode, EntityUid? user)
    {
        if (mode != MilkingMachineMode.Off && GetOccupant(ent) is { } occupant && occupant != user)
        {
            if (!_consent.Allows(occupant, ent.Comp.OthersToggle))
            {
                if (user != null)
                {
                    _popup.PopupEntity(Loc.GetString("milking-machine-no-consent",
                        ("target", Identity.Entity(occupant, EntityManager))), ent, user.Value);
                }

                return false;
            }
        }

        ent.Comp.Mode = mode;
        ent.Comp.NextUpdate = _timing.CurTime + ent.Comp.UpdateInterval;
        Dirty(ent);
        UpdateAppearance(ent);
        UpdateUI(ent);

        if (user != null)
        {
            _popup.PopupEntity(Loc.GetString("milking-machine-mode-set",
                ("mode", Loc.GetString($"milking-machine-mode-{mode.ToString().ToLowerInvariant()}"))), ent, user.Value);
        }

        return true;
    }

    private void OnStrapped(Entity<MilkingMachineComponent> ent, ref StrappedEvent args)
    {
        UpdateUI(ent);
    }

    private void OnUnstrapped(Entity<MilkingMachineComponent> ent, ref UnstrappedEvent args)
    {
        // Never keep running on the next person to sit down.
        if (ent.Comp.Mode != MilkingMachineMode.Off)
            TrySetMode(ent, MilkingMachineMode.Off, null);

        UpdateUI(ent);
    }

    private void OnUIOpened(Entity<MilkingMachineComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUI(ent);
    }

    private void OnSetModeMessage(Entity<MilkingMachineComponent> ent, ref MilkingMachineSetModeMessage args)
    {
        if (!Enum.IsDefined(args.Mode))
            return;

        TrySetMode(ent, args.Mode, args.Actor);
    }

    private void OnClimax(Entity<BuckleComponent> ent, ref ClimaxEvent args)
    {
        if (ent.Comp.BuckledTo is not { } strap || !TryComp<MilkingMachineComponent>(strap, out var machine))
            return;

        if (machine.Mode == MilkingMachineMode.Off)
            return;

        Collect((strap, machine), ent.Owner, s => s.OnClimax);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MilkingMachineComponent>();
        while (query.MoveNext(out var uid, out var machine))
        {
            if (now < machine.NextUpdate)
                continue;

            machine.NextUpdate = now + machine.UpdateInterval;

            // Plumbing drains the tank on its own schedule, so keep the fill level current even while off.
            UpdateAppearance((uid, machine));
            UpdateUI((uid, machine));
            if (machine.Mode == MilkingMachineMode.Off)
                continue;

            if (GetOccupant(uid) is not { } occupant || !_intimacy.IsEnabled(occupant))
                continue;

            var strength = machine.ModeStrength.GetValueOrDefault(machine.Mode);
            var seconds = (float) machine.UpdateInterval.TotalSeconds;
            foreach (var (stat, rate) in machine.StatsPerSecond)
                _intimacy.TryAdjustStat(occupant, stat, rate * strength * seconds);

            Collect((uid, machine), occupant, s => s.PerSecond * strength * seconds);
        }
    }

    private void Collect(Entity<MilkingMachineComponent> ent, EntityUid occupant, Func<MilkingSource, FixedPoint2> amount)
    {
        if (!_intimacy.IsEnabled(occupant) || !TryComp<IntimacyParticipantComponent>(occupant, out var participant))
            return;

        if (!_solution.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var tank, out _))
            return;

        foreach (var source in ent.Comp.Sources)
        {
            if (!participant.Features.Contains(source.Feature))
                continue;

            var quantity = amount(source);
            if (quantity > FixedPoint2.Zero)
                _solution.TryAddReagent(tank.Value, source.Reagent, quantity, out _);
        }

        UpdateAppearance(ent);
    }

    private void UpdateUI(Entity<MilkingMachineComponent> ent)
    {
        if (!_ui.IsUiOpen(ent.Owner, MilkingMachineUiKey.Key))
            return;

        var state = new MilkingMachineUiState { Mode = ent.Comp.Mode };

        if (GetOccupant(ent) is { } occupant)
        {
            state.OccupantName = Identity.Name(occupant, EntityManager);
            state.OccupantParticipates = _intimacy.IsEnabled(occupant);
            if (state.OccupantParticipates && TryComp<IntimacyParticipantComponent>(occupant, out var participant))
                state.OccupantStats = new Dictionary<string, float>(participant.Stats);
        }

        if (_solution.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out _, out var tank))
        {
            state.Volume = tank.Volume;
            state.MaxVolume = tank.MaxVolume;
            state.Contents = new List<ReagentQuantity>(tank.Contents);
        }

        _ui.SetUiState(ent.Owner, MilkingMachineUiKey.Key, state);
    }

    private void UpdateAppearance(Entity<MilkingMachineComponent> ent)
    {
        _appearance.SetData(ent, MilkingMachineVisuals.Mode, ent.Comp.Mode);

        var fill = MilkingMachineFill.Empty;
        if (_solution.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out _, out var solution) &&
            solution.MaxVolume > FixedPoint2.Zero && solution.Volume > FixedPoint2.Zero)
        {
            var fraction = (solution.Volume / solution.MaxVolume).Float();
            fill = fraction switch
            {
                >= 0.95f => MilkingMachineFill.Full,
                >= 0.6f => MilkingMachineFill.High,
                >= 0.25f => MilkingMachineFill.Medium,
                _ => MilkingMachineFill.Low,
            };
        }

        _appearance.SetData(ent, MilkingMachineVisuals.Fill, fill);
    }
}
