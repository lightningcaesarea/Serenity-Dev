using Content.Server._Serenity.Plumbing.Nodes;
using Content.Server._Starlight.Plumbing.Components;
using Content.Server._Starlight.Plumbing.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Popups;
using Content.Shared._Serenity.Plumbing;
using Content.Shared._Starlight.Plumbing.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.NodeContainer;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Serenity.Plumbing;

/// <summary>
///     Runs the valve on dockable tanks: Supply opens the tank's plumbing outlet, Fill pulls from the network
///     into the tank on each plumbing update, Closed does neither. Docking itself is handled by the nodes.
///     The valve is set from the tank's right-click menu, or from the port's while a tank is docked on it,
///     since the port sits under the tank. These are plain verbs so they don't take over a tank's alt-click.
/// </summary>
public sealed partial class PlumbingPortableSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private PlumbingPullSystem _pull = default!;
    [Dependency] private NodeContainerSystem _nodeContainer = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    private VerbCategory _valveCategory = default!;

    public override void Initialize()
    {
        base.Initialize();

        _valveCategory = new VerbCategory("plumbing-portable-verb-category", null);

        SubscribeLocalEvent<PlumbingPortableComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PlumbingPortableComponent, PlumbingDeviceUpdateEvent>(OnDeviceUpdate);
        SubscribeLocalEvent<PlumbingPortableComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<PlumbingPortableComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PlumbingFluidPortComponent, GetVerbsEvent<Verb>>(OnPortGetVerbs);
        SubscribeLocalEvent<PlumbingFluidPortComponent, ExaminedEvent>(OnPortExamined);
    }

    private void OnMapInit(Entity<PlumbingPortableComponent> ent, ref MapInitEvent args)
    {
        ApplyMode(ent);
    }

    private void OnDeviceUpdate(Entity<PlumbingPortableComponent> ent, ref PlumbingDeviceUpdateEvent args)
    {
        if (ent.Comp.Mode != PlumbingPortableMode.Fill)
            return;

        if (!_nodeContainer.TryGetNode(ent.Owner, ent.Comp.NodeName, out PlumbingPortableNode? node)
            || node.PlumbingNet is not { } net)
            return;

        if (!_solution.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var solutionEnt, out var solution)
            || solution.AvailableVolume <= 0)
            return;

        var (_, next) = _pull.PullFromNetwork(ent.Owner, net, solutionEnt.Value, ent.Comp.TransferAmount, ent.Comp.RoundRobinIndex);
        ent.Comp.RoundRobinIndex = next;
    }

    private void OnGetVerbs(Entity<PlumbingPortableComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        AddValveVerbs(ent, args.User, args.Verbs);
    }

    private void OnPortGetVerbs(Entity<PlumbingFluidPortComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        // The docked tank sits on the port and blocks the usual access check to it,
        // so check whether the user can reach the tank instead.
        if (!args.CanInteract
            || !TryGetDockedTank(ent.Owner, out var tank)
            || !_interaction.InRangeUnobstructed(args.User, tank.Owner))
            return;

        AddValveVerbs(tank, args.User, args.Verbs);
    }

    private void AddValveVerbs(Entity<PlumbingPortableComponent> tank, EntityUid user, SortedSet<Verb> verbs)
    {
        foreach (var mode in Enum.GetValues<PlumbingPortableMode>())
        {
            if (mode == tank.Comp.Mode)
                continue;

            verbs.Add(new Verb
            {
                Text = Loc.GetString("plumbing-portable-verb-set", ("mode", ModeName(mode))),
                Category = _valveCategory,
                Act = () => SetMode(tank, mode, user),
            });
        }
    }

    private void OnPortExamined(Entity<PlumbingFluidPortComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!TryGetDockedTank(ent.Owner, out var tank))
        {
            args.PushMarkup(Loc.GetString("plumbing-port-examine-empty"));
            return;
        }

        args.PushMarkup(Loc.GetString("plumbing-port-examine-docked",
            ("tank", tank.Owner),
            ("mode", ModeName(tank.Comp.Mode))));
    }

    private void OnExamined(Entity<PlumbingPortableComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(PlumbingPortableComponent)))
        {
            args.PushMarkup(Loc.GetString("plumbing-portable-examine-valve", ("mode", ModeName(ent.Comp.Mode))));
            args.PushMarkup(Loc.GetString(IsDocked(ent)
                ? "plumbing-portable-examine-docked"
                : "plumbing-portable-examine-undocked"));
        }
    }

    public void SetMode(Entity<PlumbingPortableComponent> ent, PlumbingPortableMode mode, EntityUid? user = null)
    {
        if (ent.Comp.Mode == mode)
            return;

        ent.Comp.Mode = mode;
        ApplyMode(ent);

        _audio.PlayPvs(ent.Comp.ValveSound, ent.Owner);
        if (user != null)
            _popup.PopupEntity(Loc.GetString("plumbing-portable-popup-set", ("mode", ModeName(mode))), ent.Owner, user.Value);
    }

    /// <summary>
    ///     True when the tank is anchored on a fluid connector port.
    /// </summary>
    public bool IsDocked(Entity<PlumbingPortableComponent> ent)
    {
        if (!Transform(ent.Owner).Anchored
            || !_nodeContainer.TryGetNode(ent.Owner, ent.Comp.NodeName, out PlumbingPortableNode? node))
            return false;

        foreach (var reachable in node.ReachableNodes)
        {
            if (reachable is PlumbingPortNode)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Finds the tank docked on a fluid connector port, if any.
    /// </summary>
    public bool TryGetDockedTank(EntityUid port, out Entity<PlumbingPortableComponent> tank)
    {
        tank = default;
        if (!TryComp<NodeContainerComponent>(port, out var container))
            return false;

        foreach (var node in container.Nodes.Values)
        {
            if (node is not PlumbingPortNode)
                continue;

            foreach (var reachable in node.ReachableNodes)
            {
                if (reachable is PlumbingPortableNode
                    && TryComp<PlumbingPortableComponent>(reachable.Owner, out var portable))
                {
                    tank = (reachable.Owner, portable);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    ///     Only a supplying tank is visible to other machines' pulls. A filling tank pulls through this system instead.
    /// </summary>
    private void ApplyMode(Entity<PlumbingPortableComponent> ent)
    {
        if (!TryComp<PlumbingOutletComponent>(ent.Owner, out var outlet))
            return;

        var open = ent.Comp.Mode == PlumbingPortableMode.Supply;
        if (outlet.Enabled == open)
            return;

        outlet.Enabled = open;
        Dirty(ent.Owner, outlet);
    }

    private string ModeName(PlumbingPortableMode mode)
    {
        return Loc.GetString(mode switch
        {
            PlumbingPortableMode.Supply => "plumbing-portable-mode-supply",
            PlumbingPortableMode.Fill => "plumbing-portable-mode-fill",
            _ => "plumbing-portable-mode-closed",
        });
    }
}
