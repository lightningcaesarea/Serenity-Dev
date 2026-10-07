using Content.Shared._Serenity.Consent; // Serenity
using Content.Shared._Starlight.Administration.Components;
using Content.Shared._Starlight.SocialInteraction.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Bed.Sleep;
using Content.Shared.Chat;
using Content.Shared.Ghost;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Revenant.Components;
using Content.Shared.Stunnable;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Starlight.SocialInteraction.Systems;

public sealed partial class SocialInteractionSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private SharedChatSystem _chatSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedConsentSystem _consent = default!; // Serenity

    public override void Initialize()
    {
        //subscribe to inspect events on the physical social interaction receiver component
        SubscribeLocalEvent<SocialInteractionReceiverComponent, GetVerbsEvent<Verb>>(AddSocialInteractionVerbs);
    }

    /// <summary>
    /// Adds the Social Interaction verbs to the right-click context menu.
    /// </summary>
    private void AddSocialInteractionVerbs(EntityUid uid, SocialInteractionReceiverComponent component, GetVerbsEvent<Verb> args)
    {
        // ensure the Giver is awake and alive
        if (IsDeadOrIncapacitated(args.User))
            return;

        //enumerate all the physical social interaction prototypes
        foreach (var protoid in component.InteractionPrototypes)
        {
            //resolve the proto itself
            if (!_protoMan.TryIndex<SocialInteractionPrototype>(protoid, out var proto))
                continue;

            // check if interaction needs physical contact
            if (proto.IsPhysical && !CheckInteractable(args.User, args.Target))
                continue;

            // check if this interaction allows self-targeting
            if (!proto.AllowSelfTarget && args.User == args.Target)
                continue;

            // Serenity: both parties must consent
            if (!_consent.Mutual(args.User, args.Target, proto.RequiredConsent))
                continue;

            //make a verb for each one
            Verb verb = new()
            {
                Text = Loc.GetString(proto.VerbName),
                Category = proto.OwnVerb ? null : VerbCategory.Social, // Serenity - share the Social submenu with hug
                InSocialMenu = proto.OwnVerb, // Serenity
                Icon = proto.Icon, // Serenity
                Act = () => InteractionAction(uid, args, proto)
            };

            args.Verbs.Add(verb);
        }
    }

    /// <summary>
    /// Used to check if the SocialInteractionGiver is alive and conscious,
    /// as the dead and incapaciated aren't known for being very socialable.
    /// </summary>
    private bool IsDeadOrIncapacitated(EntityUid user)
    {
        // no social interactions when ghosted, stunned, sleeping, critical or dead
        if ((HasComp<GhostComponent>(user)
            || HasComp<StunnedComponent>(user)
            || HasComp<SleepingComponent>(user)
            || HasComp<RevenantComponent>(user) // revenants are ghosts
            || _mobStateSystem.IsIncapacitated(user)) && !HasComp<AdminGhostComponent>(user)) // admin ghosts are exempt!
            return true;

        return false;
    }

    /// <summary>
    /// Used to check if a physical interaction is possible.
    /// </summary>
    private bool CheckInteractable(EntityUid user, EntityUid target)
    {
        if (!_actionBlockerSystem.CanInteract(user, target))
            return false;

        if (!_interactionSystem.InRangeUnobstructed(user, target))
            return false;

        return true;
    }

    /// <summary>
    /// Performs our Social Interaction
    /// </summary>
    private void InteractionAction(EntityUid uid, GetVerbsEvent<Verb> args, SocialInteractionPrototype proto)
    {
        // ensure the Giver is awake and alive
        if (IsDeadOrIncapacitated(args.User))
            return;

        // needed to not play interaction audio multiple times
        if (!_timing.IsFirstTimePredicted)
            return;

        // check if interaction needs physical contact
        if (proto.IsPhysical && !CheckInteractable(args.User, args.Target))
            return;

        // Serenity: re-check consent at execution time
        if (!_consent.Mutual(args.User, args.Target, proto.RequiredConsent))
            return;

        if (!TryComp<SocialInteractionGiverComponent>(args.User, out var giverComp))
            return;

        var curTime = _timing.CurTime;

        // prevent spamming interactions
        if (giverComp.LastInteractTime is { } lastInteractTime
            && curTime < lastInteractTime + proto.InteractDelay)
            return;

        giverComp.LastInteractTime = curTime;

        var selfTarget = args.User == args.Target; // whether or not we're interacting with ourselves
        var msg = ""; // Stores the text to be shown in the popup message
        SoundSpecifier? sfx = null; // Stores the filepath of the sound to be played

        if (proto.InteractString != null)
            msg = Loc.GetString(proto.InteractString, ("target", Identity.Entity(args.Target, EntityManager)));

        if (proto.InteractSound != null)
            sfx = proto.InteractSound;

        // pop-up message for the target - skip if self targeted
        if (!selfTarget && proto.MessagePerceivedByOthers is { } message)
        {
            var msgOthers = Loc.GetString(message,
                ("user", Identity.Entity(args.User, EntityManager)),
                ("target", Identity.Entity(args.Target, EntityManager)));

            var othersFilter = Filter.PvsExcept(args.User, entityManager: EntityManager);
            // Serenity - the target gets its own message instead
            if (proto.MessagePerceivedByTarget != null)
                othersFilter.RemovePlayerByAttachedEntity(args.Target);

            _popupSystem.PopupEntity(msgOthers, uid, othersFilter, true);
        }

        // Serenity - popup only the target sees
        if (!selfTarget && proto.MessagePerceivedByTarget is { } targetMessage)
        {
            var msgTarget = Loc.GetString(targetMessage,
                ("user", Identity.Entity(args.User, EntityManager)),
                ("target", Identity.Entity(args.Target, EntityManager)));

            _popupSystem.PopupEntity(msgTarget, uid, args.Target);
        }

        // emote message for chat
        if (proto.EmoteMessage is { } emoteMessage)
        {
            // show a different message if we're using the emote on ourselves
            var emoteTarget = selfTarget
                ? proto.EmoteMessageSelf ?? emoteMessage
                : emoteMessage;

            // resolve localization
            var emote = Loc.GetString(
                emoteTarget,
                ("user", Identity.Entity(args.User, EntityManager)),
                ("target", Identity.Entity(args.Target, EntityManager)));

            // post emote
            _chatSystem.TrySendInGameICMessage(args.User, emote, InGameICChatType.Emote, ChatTransmitRange.Normal);
        }

        // now popup filtered to user - skip if it's self-targeted
        if (!selfTarget)
            _popupSystem.PopupClient(msg, uid, args.User);

        if (proto.SoundPerceivedByOthers)
        {
            _audio.PlayPredicted(sfx, Transform(args.Target).Coordinates, args.User);
        }
        else
        {
            _audio.PlayLocal(sfx, args.Target, args.User);

            // don't play sounds twice if you're the target
            if (args.User != args.Target)
                _audio.PlayEntity(sfx, Filter.Entities(args.Target), args.Target, false);
        }
    }
}
