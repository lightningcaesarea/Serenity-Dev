using Content.Shared._Serenity.Plumbing;
using Content.Shared._Starlight.Plumbing.Components;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Serenity.Plumbing;

/// <summary>
///     Applies a <see cref="PlumbingPumpComponent"/>'s switch and rate to the machine's plumbing inlet,
///     and serves its control window.
/// </summary>
public sealed partial class PlumbingPumpSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlumbingPumpComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PlumbingPumpComponent, BoundUIOpenedEvent>(OnUIOpened);
        SubscribeLocalEvent<PlumbingPumpComponent, PlumbingPumpToggleMessage>(OnToggle);
        SubscribeLocalEvent<PlumbingPumpComponent, PlumbingPumpSetRateMessage>(OnSetRate);
        SubscribeLocalEvent<PlumbingPumpComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(Entity<PlumbingPumpComponent> ent, ref MapInitEvent args)
    {
        Apply(ent);
    }

    private void OnUIOpened(Entity<PlumbingPumpComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUI(ent);
    }

    private void OnToggle(Entity<PlumbingPumpComponent> ent, ref PlumbingPumpToggleMessage args)
    {
        SetEnabled(ent, args.Enabled);
        _audio.PlayPvs(ent.Comp.ClickSound, ent.Owner, AudioParams.Default.WithVolume(-2f));
    }

    private void OnSetRate(Entity<PlumbingPumpComponent> ent, ref PlumbingPumpSetRateMessage args)
    {
        SetTransferAmount(ent, args.TransferAmount);
        _audio.PlayPvs(ent.Comp.ClickSound, ent.Owner, AudioParams.Default.WithVolume(-2f));
    }

    private void OnExamined(Entity<PlumbingPumpComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(ent.Comp.Enabled
            ? Loc.GetString("plumbing-pump-examine-on", ("rate", ent.Comp.TransferAmount))
            : Loc.GetString("plumbing-pump-examine-off"));
    }

    public void SetEnabled(Entity<PlumbingPumpComponent> ent, bool enabled)
    {
        ent.Comp.Enabled = enabled;
        Apply(ent);
        UpdateUI(ent);
    }

    /// <summary>
    ///     Sets the rate, clamped to 0 and the pump's maximum. Fractions are rounded to the nearest unit.
    /// </summary>
    public void SetTransferAmount(Entity<PlumbingPumpComponent> ent, float amount)
    {
        if (!float.IsFinite(amount))
            return;

        var rounded = FixedPoint2.New(MathF.Round(amount));
        ent.Comp.TransferAmount = FixedPoint2.Clamp(rounded, FixedPoint2.Zero, ent.Comp.MaxTransferAmount);
        Apply(ent);
        UpdateUI(ent);
    }

    private void Apply(Entity<PlumbingPumpComponent> ent)
    {
        if (TryComp<PlumbingInletComponent>(ent.Owner, out var inlet))
            inlet.TransferAmount = ent.Comp.Enabled ? ent.Comp.TransferAmount : FixedPoint2.Zero;

        _appearance.SetData(ent.Owner, PlumbingPumpVisuals.Enabled, ent.Comp.Enabled);
    }

    private void UpdateUI(Entity<PlumbingPumpComponent> ent)
    {
        _ui.SetUiState(ent.Owner,
            PlumbingPumpUiKey.Key,
            new PlumbingPumpBoundUserInterfaceState(
                ent.Comp.Enabled,
                ent.Comp.TransferAmount.Float(),
                ent.Comp.MaxTransferAmount.Float()));
    }
}
