using Content.Server.Chat.Systems;
using Content.Shared._Serenity.Intimacy;
using Content.Shared.Chat;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Serenity.Intimacy;

/// <summary>
/// Drives <see cref="IntimacyVocalComponent"/>: moans when pleasured and automatically while aroused,
/// pained sounds when Pain spikes, a blush when acted upon, and a sex-appropriate climax cry, using
/// thresholds, a per-second chance and sex-tiered sounds.
/// </summary>
public sealed partial class IntimacyVocalSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IntimacySystem _intimacy = default!;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private static readonly List<LocId> NoMessages = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IntimacyVocalComponent, IntimacyStatChangedEvent>(OnStatChanged);
        SubscribeLocalEvent<IntimacyVocalComponent, IntimacyActPerformedEvent>(OnActPerformed);
        SubscribeLocalEvent<IntimacyVocalComponent, ClimaxEvent>(OnClimax);
    }

    private void OnStatChanged(Entity<IntimacyVocalComponent> ent, ref IntimacyStatChangedEvent args)
    {
        // Pain rising past the threshold draws a pained sound; decay and relief never do.
        if (args.Stat != "Pain" || args.New <= args.Old || args.New < ent.Comp.PainThreshold)
            return;

        if (!_random.Prob(ent.Comp.PainMoanChance))
            return;

        ent.Comp.PainMoans.TryGetValue(GetSex(ent), out var sound);
        Vocalise(ent, sound, ent.Comp.PainMessages);
    }

    private void OnActPerformed(Entity<IntimacyVocalComponent> ent, ref IntimacyActPerformedEvent args)
    {
        // Only the receiving side reacts: the target of someone else's act, or the actor of a self act.
        if (args.Target != ent.Owner)
            return;

        var self = args.Actor == ent.Owner;
        var stats = self ? args.Act.ActorStats : args.Act.TargetStats;
        var pleasurable = stats.TryGetValue("Pleasure", out var pleasure) && pleasure > 0
            || stats.TryGetValue("Arousal", out var aroused) && aroused > 0;

        // A pleasurable act on an aroused mob draws a moan. The act already posted its own emote,
        // so this one is sound only.
        if (pleasurable
            && TryComp<IntimacyParticipantComponent>(ent, out var participant)
            && !_mobState.IsIncapacitated(ent))
        {
            var arousal = participant.Stats.GetValueOrDefault("Arousal");
            if (arousal >= ent.Comp.ActMoanArousalThreshold && _random.Prob(ent.Comp.ActMoanChance))
            {
                Vocalise(ent, GetMoanSound(ent, arousal), NoMessages);
                return;
            }
        }

        // Blushing is for when someone else did it.
        if (self || !_random.Prob(ent.Comp.BlushChance))
            return;

        Vocalise(ent, ent.Comp.Blush, ent.Comp.BlushMessages);
    }

    private void OnClimax(Entity<IntimacyVocalComponent> ent, ref ClimaxEvent args)
    {
        // ClimaxSystem already played an explicit per-entity sound if one was configured.
        if (TryComp<ClimaxComponent>(ent, out var climax) && climax.Sound != null)
            return;

        if (ent.Comp.Climax.TryGetValue(GetSex(ent), out var sound))
            _intimacy.PlayLewdSound(sound, ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<IntimacyVocalComponent, IntimacyParticipantComponent>();
        while (query.MoveNext(out var uid, out var vocal, out var participant))
        {
            if (now < vocal.NextTick)
                continue;
            vocal.NextTick = now + TickInterval;

            if (participant.Stats.Count == 0 || !_intimacy.IsEnabled(uid) || _mobState.IsIncapacitated(uid))
                continue;

            var arousal = participant.Stats.GetValueOrDefault("Arousal");
            var pleasure = participant.Stats.GetValueOrDefault("Pleasure");

            var liable = arousal >= vocal.MoanArousalThreshold || pleasure >= vocal.MoanPleasureThreshold;
            if (!liable || !_random.Prob(vocal.AutoMoanChance))
                continue;

            var messages = arousal >= vocal.MoanArousalThreshold ? vocal.HighMoanMessages : vocal.MoanMessages;
            Vocalise((uid, vocal), GetMoanSound((uid, vocal), arousal), messages);
        }
    }

    /// <summary>The moan for the mob's sex at its current arousal, or null if none is configured.</summary>
    private SoundSpecifier? GetMoanSound(Entity<IntimacyVocalComponent> ent, float arousal)
    {
        if (!ent.Comp.Moans.TryGetValue(GetSex(ent), out var tiers))
            return null;

        foreach (var tier in tiers)
        {
            if (arousal >= tier.MinArousal)
                return tier.Sound;
        }

        return null;
    }

    /// <summary>Plays a consent-filtered sound and emits an emote line, rate-limited by <see cref="IntimacyVocalComponent.MinimumGap"/>.</summary>
    private void Vocalise(Entity<IntimacyVocalComponent> ent, SoundSpecifier? sound, List<LocId> messages)
    {
        var now = _timing.CurTime;
        if (now < ent.Comp.NextVocal)
            return;
        ent.Comp.NextVocal = now + ent.Comp.MinimumGap;

        _intimacy.PlayLewdSound(sound, ent);

        if (messages.Count == 0)
            return;

        var text = Loc.GetString(_random.Pick(messages), ("actor", Identity.Entity(ent, EntityManager)));
        _chat.TrySendInGameICMessage(ent, text, InGameICChatType.Emote, ChatTransmitRange.Normal);
    }

    private Sex GetSex(Entity<IntimacyVocalComponent> ent)
    {
        if (ent.Comp.SexOverride is { } forced)
            return forced;

        return TryComp<HumanoidAppearanceComponent>(ent, out var humanoid) ? humanoid.Sex : Sex.Unsexed;
    }
}
