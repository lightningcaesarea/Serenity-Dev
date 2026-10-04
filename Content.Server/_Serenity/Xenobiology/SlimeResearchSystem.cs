using Content.Server.Popups;
using Content.Server.Research.Systems;
using Content.Shared._Serenity.Xenobiology;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._Serenity.Xenobiology;

/// <summary>
/// Pays research points for the extracts a slime processor produces. See <see cref="SlimeProcessorResearchComponent"/>.
/// </summary>
public sealed partial class SlimeResearchSystem : EntitySystem
{
    [Dependency] private ResearchSystem _research = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private TagSystem _tag = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SlimeProcessorResearchComponent, SlimeProcessedEvent>(OnSlimeProcessed);
    }

    private void OnSlimeProcessed(Entity<SlimeProcessorResearchComponent> ent, ref SlimeProcessedEvent args)
    {
        if (args.Extracts.Count == 0)
            return;

        if (!TryGetTierPoints(ent.Comp, args.Extracts[0], out var tierPoints))
            return;

        if (!_research.TryGetClientServer(ent, out var server, out var serverComp))
            return;

        var ledger = EnsureComp<SlimeResearchLedgerComponent>(server.Value);
        var processed = ledger.Processed.GetValueOrDefault(args.Extract);
        var total = 0;
        foreach (var _ in args.Extracts)
        {
            total += GetPoints(ent.Comp, tierPoints, processed);
            processed++;
        }
        ledger.Processed[args.Extract] = processed;

        if (total <= 0)
            return;

        _research.ModifyServerPoints(server.Value, total, serverComp);
        _popup.PopupEntity(Loc.GetString("slime-processor-research-points", ("points", total)), ent);
    }

    /// <summary>
    /// The tier value of an extract, from the first of its tags the processor has a value for.
    /// </summary>
    public bool TryGetTierPoints(SlimeProcessorResearchComponent comp, EntityUid extract, out int points)
    {
        foreach (var (tag, value) in comp.TierPoints)
        {
            if (!_tag.HasTag(extract, tag))
                continue;

            points = value;
            return true;
        }

        points = 0;
        return false;
    }

    /// <summary>
    /// Points for one extract of a colour when <paramref name="alreadyProcessed"/> extracts of that colour
    /// have already been processed for the same research server.
    /// </summary>
    public static int GetPoints(SlimeProcessorResearchComponent comp, int tierPoints, int alreadyProcessed)
    {
        var points = tierPoints * MathF.Pow(comp.Decay, alreadyProcessed);
        if (alreadyProcessed == 0)
            points += tierPoints * comp.DiscoveryBonus;

        return (int) MathF.Round(points);
    }
}

/// <summary>
/// Added to a research server the first time a slime processor pays it. Counts extracts per colour so
/// repeats pay less. The server is rebuilt each round, so this resets with it.
/// </summary>
[RegisterComponent, Access(typeof(SlimeResearchSystem))]
public sealed partial class SlimeResearchLedgerComponent : Component
{
    [ViewVariables]
    public Dictionary<EntProtoId, int> Processed = new();
}
