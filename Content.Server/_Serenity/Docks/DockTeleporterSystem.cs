using Content.Shared._Serenity.Docks;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.GameObjects;

namespace Content.Server._Serenity.Docks;

/// <summary>Sends the user to the partner pad when a <see cref="DockTeleporterComponent"/> is activated.</summary>
public sealed partial class DockTeleporterSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DockTeleporterComponent, ActivateInWorldEvent>(OnActivate);
    }

    /// <summary>Links two pads so each sends people to the other.</summary>
    public void Link(Entity<DockTeleporterComponent> a, Entity<DockTeleporterComponent> b)
    {
        a.Comp.Partner = b.Owner;
        b.Comp.Partner = a.Owner;
    }

    private void OnActivate(Entity<DockTeleporterComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;

        if (ent.Comp.Partner is not { } partner || TerminatingOrDeleted(partner))
        {
            _popup.PopupEntity(Loc.GetString("dock-teleporter-no-link"), ent, args.User);
            return;
        }

        _transform.SetCoordinates(args.User, Transform(partner).Coordinates);
        _transform.AttachToGridOrMap(args.User);
    }
}
