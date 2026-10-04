using Content.Shared.Chemistry.Components;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Shared._Serenity.Chemistry;

/// <summary>
/// Applies and toggles the fill/dispense mode of <see cref="ToggleableSolutionTransferComponent"/>.
/// </summary>
public sealed partial class ToggleableSolutionTransferSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly SpriteSpecifier VerbIcon =
        new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/refresh.svg.192dpi.png"));

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ToggleableSolutionTransferComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ToggleableSolutionTransferComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<ToggleableSolutionTransferComponent, ExaminedEvent>(OnExamined);

        // Neither component networks its fields, so a copy added by the server arrives on the client
        // with the default solution name. Point it at the right solution on both sides.
        SubscribeLocalEvent<RefillableSolutionComponent, ComponentInit>(OnRefillableInit);
        SubscribeLocalEvent<DrainableSolutionComponent, ComponentInit>(OnDrainableInit);
    }

    private void OnMapInit(Entity<ToggleableSolutionTransferComponent> ent, ref MapInitEvent args)
    {
        ApplyMode(ent);
    }

    private void OnGetVerbs(Entity<ToggleableSolutionTransferComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(ent.Comp.Filling
                ? "toggleable-transfer-verb-dispense"
                : "toggleable-transfer-verb-fill"),
            Icon = VerbIcon,
            Act = () => SetFilling(ent, !ent.Comp.Filling, user),
        });
    }

    private void OnExamined(Entity<ToggleableSolutionTransferComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString(ent.Comp.Filling
            ? "toggleable-transfer-examine-filling"
            : "toggleable-transfer-examine-dispensing"));
    }

    private void OnRefillableInit(Entity<RefillableSolutionComponent> ent, ref ComponentInit args)
    {
        if (TryComp<ToggleableSolutionTransferComponent>(ent, out var toggle))
            ent.Comp.Solution = toggle.Solution;
    }

    private void OnDrainableInit(Entity<DrainableSolutionComponent> ent, ref ComponentInit args)
    {
        if (TryComp<ToggleableSolutionTransferComponent>(ent, out var toggle))
            ent.Comp.Solution = toggle.Solution;
    }

    public void SetFilling(Entity<ToggleableSolutionTransferComponent> ent, bool filling, EntityUid? user = null)
    {
        if (ent.Comp.Filling == filling)
            return;

        ent.Comp.Filling = filling;
        Dirty(ent);
        ApplyMode(ent);

        if (user != null)
        {
            _popup.PopupClient(Loc.GetString(filling
                    ? "toggleable-transfer-popup-filling"
                    : "toggleable-transfer-popup-dispensing"),
                ent,
                user.Value);
        }
    }

    private void ApplyMode(Entity<ToggleableSolutionTransferComponent> ent)
    {
        if (ent.Comp.Filling)
        {
            RemComp<DrainableSolutionComponent>(ent);
            EnsureComp<RefillableSolutionComponent>(ent).Solution = ent.Comp.Solution;
        }
        else
        {
            RemComp<RefillableSolutionComponent>(ent);
            EnsureComp<DrainableSolutionComponent>(ent).Solution = ent.Comp.Solution;
        }
    }
}
