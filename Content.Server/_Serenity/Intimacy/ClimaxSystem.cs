using Content.Shared._Serenity.Consent;
using Content.Shared._Serenity.Intimacy;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Serenity.Intimacy;

public sealed partial class ClimaxSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedChatSystem _chat = default!;
    [Dependency] private SharedConsentSystem _consent = default!;
    [Dependency] private SharedIntimacySystem _intimacy = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClimaxComponent, IntimacyStatChangedEvent>(OnStatChanged);
    }

    private void OnStatChanged(Entity<ClimaxComponent> ent, ref IntimacyStatChangedEvent args)
    {
        if (args.Stat != ent.Comp.Stat.Id || args.New < ent.Comp.AutoThreshold)
            return;

        if (!_consent.Allows(ent, SharedIntimacySystem.AutoClimaxToggle))
            return;

        TryClimax(ent.Owner, true);
    }

    public bool CanClimax(Entity<ClimaxComponent?> mob, bool automatic)
    {
        if (!Resolve(mob, ref mob.Comp, false) || !_intimacy.IsEnabled(mob))
            return false;

        if (_timing.CurTime < mob.Comp.ReadyAt)
            return false;

        var threshold = automatic ? mob.Comp.AutoThreshold : mob.Comp.ManualThreshold;
        return _intimacy.GetStat(mob.Owner, mob.Comp.Stat) >= threshold;
    }

    public bool TryClimax(Entity<ClimaxComponent?> mob, bool automatic)
    {
        // Resolve here: CanClimax resolves into its own copy of the entity, so mob.Comp would stay null.
        if (!Resolve(mob, ref mob.Comp, false) || !CanClimax(mob, automatic))
            return false;

        foreach (var (stat, value) in mob.Comp.After)
            _intimacy.TrySetStat(mob.Owner, stat, value);

        mob.Comp.ReadyAt = _timing.CurTime + mob.Comp.Refractory;
        Dirty(mob);

        if (mob.Comp.Messages.Count > 0)
        {
            var text = Loc.GetString(_random.Pick(mob.Comp.Messages), ("actor", Identity.Entity(mob, EntityManager)));
            _chat.TrySendInGameICMessage(mob, text, InGameICChatType.Emote, ChatTransmitRange.Normal);
        }

        if (_intimacy is IntimacySystem server)
            server.PlayLewdSound(mob.Comp.Sound, mob);

        var ev = new ClimaxEvent(mob, automatic);
        RaiseLocalEvent(mob, ref ev);
        return true;
    }
}
