using Content.Shared._Serenity.Consent;
using Content.Shared._Serenity.Intimacy;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Serenity.Intimacy;

/// <summary>
/// Runs the intimacy loop: opens the window, executes acts, decays stats, keeps windows fresh.
/// </summary>
public sealed partial class IntimacySystem : SharedIntimacySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedChatSystem _chat = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ClimaxSystem _climax = default!;

    private static readonly TimeSpan DecayInterval = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IntimacyParticipantComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<IntimacyParticipantComponent, IntimacyPerformActMessage>(OnPerformAct);
        SubscribeLocalEvent<IntimacyParticipantComponent, IntimacyClimaxMessage>(OnClimaxRequest);
        SubscribeLocalEvent<IntimacyParticipantComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<IntimacyParticipantComponent, ConsentChangedEvent>(OnConsentChanged);
    }

    private void OnGetVerbs(Entity<IntimacyParticipantComponent> target, ref GetVerbsEvent<Verb> args)
    {
        var user = args.User;
        if (!IsEnabled(user) || !IsEnabled(target))
            return;

        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        var session = actor.PlayerSession;
        var targetUid = target.Owner;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("intimacy-verb"),
            Priority = -10,
            Act = () => OpenWindow(user, targetUid, session),
            Category = VerbCategory.Social,
        });
    }

    private void OpenWindow(EntityUid user, EntityUid target, ICommonSession session)
    {
        if (!TryComp<IntimacyParticipantComponent>(user, out var comp))
            return;

        comp.WindowTarget = target;
        _ui.OpenUi(user, IntimacyUiKey.Key, session);
        RefreshWindow((user, comp));
    }

    private void OnUiClosed(Entity<IntimacyParticipantComponent> ent, ref BoundUIClosedEvent args)
    {
        if (args.UiKey is IntimacyUiKey.Key)
            ent.Comp.WindowTarget = null;
    }

    private void OnConsentChanged(Entity<IntimacyParticipantComponent> ent, ref ConsentChangedEvent args)
    {
        // Withdrawing the master toggle drops every value so nothing lingers if it is re-enabled later.
        if (Consent.Allows(ent, MasterToggle))
            return;

        if (ent.Comp.Stats.Count > 0)
        {
            ent.Comp.Stats.Clear();
            Dirty(ent);
        }

        if (ent.Comp.WindowTarget != null)
            _ui.CloseUi(ent.Owner, IntimacyUiKey.Key);
    }

    private void OnPerformAct(Entity<IntimacyParticipantComponent> actor, ref IntimacyPerformActMessage args)
    {
        if (args.Actor != actor.Owner || actor.Comp.WindowTarget is not { } target)
            return;

        if (!Proto.TryIndex<IntimacyActPrototype>(args.Act, out var act))
            return;

        if (!CanPerform(actor, target, act, out var reason))
        {
            _popup.PopupEntity(Loc.GetString(reason), actor, actor);
            RefreshWindow(actor);
            return;
        }

        Execute(actor, target, act);
        RefreshWindow(actor);
    }

    private void OnClimaxRequest(Entity<IntimacyParticipantComponent> actor, ref IntimacyClimaxMessage args)
    {
        if (args.Actor != actor.Owner)
            return;

        if (!_climax.TryClimax(actor.Owner, false))
            _popup.PopupEntity(Loc.GetString("intimacy-fail-climax"), actor, actor);

        RefreshWindow(actor);
    }

    /// <summary>
    /// Applies an already-validated act.
    /// </summary>
    private void Execute(Entity<IntimacyParticipantComponent> actor, EntityUid target, IntimacyActPrototype act)
    {
        var self = actor.Owner == target;

        foreach (var (stat, delta) in act.ActorStats)
            TryAdjustStat(actor.Owner, stat, delta);

        if (!self)
        {
            foreach (var (stat, delta) in act.TargetStats)
                TryAdjustStat(target, stat, delta);
        }

        actor.Comp.ReadyAt[act.ID] = Timing.CurTime + act.Cooldown;

        var lines = self && act.SelfMessages.Count > 0 ? act.SelfMessages : act.Messages;
        if (lines.Count > 0)
        {
            var text = Loc.GetString(_random.Pick(lines),
                ("actor", Identity.Entity(actor, EntityManager)),
                ("target", Identity.Entity(target, EntityManager)));
            _chat.TrySendInGameICMessage(actor, text, InGameICChatType.Emote, ChatTransmitRange.Normal);
        }

        PlayLewdSound(act.Sound, target);

        var ev = new IntimacyActPerformedEvent(actor, target, act);
        RaiseLocalEvent(actor, ref ev);
        if (!self)
            RaiseLocalEvent(target, ref ev);
    }

    /// <summary>
    /// Plays a sound at <paramref name="source"/> only for listeners who consent to hearing it.
    /// </summary>
    public void PlayLewdSound(SoundSpecifier? sound, EntityUid source)
    {
        if (sound == null)
            return;

        var filter = Filter.Pvs(source, entityManager: EntityManager)
            .RemoveWhere(s => s.AttachedEntity is not { } listener || !Consent.Allows(listener, SoundsToggle));

        _audio.PlayEntity(sound, filter, source, true);
    }

    public void RefreshWindow(Entity<IntimacyParticipantComponent> actor)
    {
        if (actor.Comp.WindowTarget is not { } target || !_ui.IsUiOpen(actor.Owner, IntimacyUiKey.Key))
            return;

        if (TerminatingOrDeleted(target) || !IsEnabled(target))
        {
            _ui.CloseUi(actor.Owner, IntimacyUiKey.Key);
            return;
        }

        var state = new IntimacyUiState
        {
            Target = GetNetEntity(target),
            TargetName = Identity.Name(target, EntityManager, actor),
            ActorStats = new Dictionary<string, float>(actor.Comp.Stats),
            TargetStats = new Dictionary<string, float>(Comp<IntimacyParticipantComponent>(target).Stats),
            CanClimax = _climax.CanClimax(actor.Owner, false),
        };

        foreach (var act in AvailableActs(actor, target))
            state.Available.Add(act.ID);

        _ui.SetUiState(actor.Owner, IntimacyUiKey.Key, state);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = Timing.CurTime;
        var query = EntityQueryEnumerator<IntimacyParticipantComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextDecay)
                continue;

            comp.NextDecay = now + DecayInterval;

            if (comp.Stats.Count > 0 && IsEnabled(uid))
            {
                foreach (var stat in new List<string>(comp.Stats.Keys))
                {
                    if (!Proto.TryIndex<IntimacyStatPrototype>(stat, out var proto))
                        continue;

                    TryAdjustStat((uid, comp), stat, -proto.DecayPerSecond * (float) DecayInterval.TotalSeconds);
                }
            }

            if (comp.WindowTarget != null)
                RefreshWindow((uid, comp));
        }
    }
}
