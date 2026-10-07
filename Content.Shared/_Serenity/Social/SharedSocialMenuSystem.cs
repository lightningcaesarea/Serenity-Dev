using Content.Shared.Bed.Sleep;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Verbs;

namespace Content.Shared._Serenity.Social;

/// <summary>
/// Turns an empty-hand click on another player into a condensed social verb menu. The hug that the click
/// used to do becomes one of the menu's entries.
/// </summary>
public abstract partial class SharedSocialMenuSystem : EntitySystem
{
    [Dependency] private InteractionPopupSystem _interactionPopup = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SocialMenuComponent, InteractHandEvent>(OnInteractHand,
            before: new[] { typeof(InteractionPopupSystem) });
        SubscribeLocalEvent<SocialMenuComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnInteractHand(EntityUid uid, SocialMenuComponent component, InteractHandEvent args)
    {
        // NPCs (hugbots and the like) keep the old instant hug.
        if (args.Handled || args.User == args.Target || !IsPlayer(args.User))
            return;

        args.Handled = true;
        OpenMenu(args.User, uid);
    }

    /// <summary>
    /// Whether <paramref name="user"/> is driven by a player who can see the menu.
    /// </summary>
    protected abstract bool IsPlayer(EntityUid user);

    /// <summary>
    /// Opens the social menu for <paramref name="user"/>. Only the client draws it; the server just
    /// swallows the click so the old hug does not fire.
    /// </summary>
    protected virtual void OpenMenu(EntityUid user, EntityUid target)
    {
    }

    private void OnGetVerbs(Entity<SocialMenuComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.User == args.Target)
            return;

        if (!HasComp<InteractionPopupComponent>(ent)
            || HasComp<SleepingComponent>(ent)
            || !_mobState.IsAlive(ent))
            return;

        var user = args.User;
        var target = ent.Owner;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("social-verb-hug"),
            Priority = 10,
            Act = () => _interactionPopup.TryInteract(target, user),
            Category = VerbCategory.Social,
        });
    }
}
