using Content.Shared.Botany.Components;
using Content.Shared.Botany.Events;
using Content.Shared.Botany.Systems;
using Content.Shared.Popups;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using JetBrains.Annotations;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._Serenity.Botany;

/// <summary>
/// Drives plant instability: the random mutations an unstable plant goes through each growth cycle, and the
/// outcome roll mutagens trigger. Other systems change instability through <see cref="AdjustInstability"/>.
/// </summary>
public sealed partial class PlantInstabilitySystem : EntitySystem
{
    private static readonly ProtoId<WeightedRandomFillSolutionPrototype>[] TraitChemTables =
    [
        "RandomPickBotanyGeneralReagent",
        "RandomPickBotanyFarmingReagent",
    ];

    [Dependency] private BotanySystem _botany = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PlantChemicalsSystem _chemicals = default!;
    [Dependency] private PlantHolderSystem _holder = default!;
    [Dependency] private PlantMutationSystem _mutation = default!;
    [Dependency] private PlantSystem _plant = default!;
    [Dependency] private PlantTraySystem _tray = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    [Dependency] private EntityQuery<PlantComponent> _plantQuery;
    [Dependency] private EntityQuery<PlantDataComponent> _dataQuery;
    [Dependency] private EntityQuery<PlantInstabilityComponent> _instabilityQuery;

    [SubscribeLocalEvent]
    private void OnCrossPollinate(Entity<PlantInstabilityComponent> ent, ref PlantCrossPollinateEvent args)
    {
        if (!_botany.TryGetPlantComponent<PlantInstabilityComponent>(args.PollenData, args.PollenProtoId, out var pollen))
            return;

        ent.Comp.Instability = Math.Clamp((ent.Comp.Instability + pollen.Instability) / 2f, 0f, ent.Comp.MaxInstability);
        Dirty(ent);
    }

    [SubscribeLocalEvent]
    private void OnPlantGrow(Entity<PlantInstabilityComponent> ent, ref PlantGrowEvent args)
    {
        if (!_plantQuery.HasComp(ent))
            return;

        var comp = ent.Comp;
        var instability = comp.Instability;
        if (instability < comp.SoftThreshold)
            return;

        var random = SharedRandomExtensions.PredictedRandom(_timing, GetNetEntity(ent), GetNetEntity(args.Tray));

        if (instability >= comp.SpeciesThreshold
            && random.Prob(instability * comp.SpeciesChancePerPoint)
            && TryChangeSpecies(ent, args.Tray, random))
        {
            return;
        }

        if (instability >= comp.TraitThreshold
            && random.Prob((instability - comp.TraitBaseline) * comp.TraitChancePerPoint))
        {
            _chemicals.MutateRandomChemical(ent.Owner, TraitChemTables);
        }

        if (random.Prob(instability * comp.StatChancePerPoint))
            MutateStats(ent, instability >= comp.HardThreshold, random);
    }

    private bool TryChangeSpecies(Entity<PlantInstabilityComponent> ent, EntityUid tray, IRobustRandom random)
    {
        if (!_dataQuery.TryComp(ent, out var data) || data.MutationPrototypes.Count == 0)
            return false;

        // The mutation is only carried out by the server, so the client has nothing to predict here.
        if (!_net.IsServer)
            return true;

        var species = random.Pick(data.MutationPrototypes);

        // The new species starts over: instability halves and the tray's weeds are gone.
        ent.Comp.Instability /= 2f;
        Dirty(ent);

        if (TryComp<PlantTrayComponent>(tray, out var trayComp))
            _tray.AdjustWeed((tray, trayComp), -trayComp.WeedLevel);

        _mutation.SpeciesChange((ent.Owner, data), species);
        return true;
    }

    /// <summary>
    /// Changes the plant's instability, keeping it within its range.
    /// </summary>
    [PublicAPI]
    public void AdjustInstability(Entity<PlantInstabilityComponent?> ent, float amount)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        ent.Comp.Instability = Math.Clamp(ent.Comp.Instability + amount, 0f, ent.Comp.MaxInstability);
        DirtyField(ent, nameof(ent.Comp.Instability));
    }

    /// <summary>
    /// Nudges the plant's stats up or down at random. A hard mutation moves them twice as far.
    /// </summary>
    [PublicAPI]
    public void MutateStats(Entity<PlantInstabilityComponent> ent, bool hard, IRobustRandom random)
    {
        if (!_plantQuery.TryComp(ent, out var plant))
            return;

        var factor = hard ? 2 : 1;
        int Jitter(int amount) => random.Next(-amount * factor, amount * factor + 1);

        var target = new Entity<PlantComponent?>(ent.Owner, plant);

        _plant.AdjustLifespan(target, Bounded(plant.Lifespan, Jitter(2), 10, 100));
        _plant.AdjustEndurance(target, Bounded(plant.Endurance, Jitter(5), 10, 100));
        _plant.AdjustProduction(target, Bounded(plant.Production, Jitter(1), 1, 10));
        _plant.AdjustYield(target, plant.Yield > 0 ? Bounded(plant.Yield, Jitter(1), 1, 10) : 0);
        _plant.AdjustPotency(target, Bounded(plant.Potency, Jitter(15), 0, 100));

        AdjustInstability(ent.AsNullable(), Jitter(3));
    }

    /// <summary>
    /// The change that moves <paramref name="current"/> by <paramref name="delta"/> without leaving the range.
    /// A stat that already sits outside the range is never pushed further out of it.
    /// </summary>
    private static int Bounded(float current, int delta, int min, int max)
    {
        var wanted = current + delta;
        var low = Math.Min(min, current);
        var high = Math.Max(max, current);
        return (int) (Math.Clamp(wanted, low, high) - current);
    }

    /// <summary>
    /// Rolls what a dose of mutagen does to the plant: mostly it destabilises it, sometimes it hurts it, and a
    /// plant that is already overrun with weeds or pests suffers for it.
    /// </summary>
    [PublicAPI]
    public void RollMutagenOutcome(Entity<PlantInstabilityComponent> ent)
    {
        if (_holder.IsDead(ent.Owner))
            return;

        _plant.TryGetTray(ent.Owner, out var tray);
        var random = SharedRandomExtensions.PredictedRandom(_timing, GetNetEntity(ent), GetNetEntity(tray.Owner));
        var roll = random.Next(0, 100);

        if (roll >= 90)
        {
            _holder.AdjustsHealth(ent.Owner, -10f);
        }
        else if (roll >= 40)
        {
            AdjustInstability(ent.AsNullable(), 5f);
        }
        else if (roll >= 20)
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("plant-mutagen-shudder"), ent.Owner, PopupType.Small);
        }
        else if (roll >= 10)
        {
            if (tray.Comp != null && tray.Comp.WeedLevel > 5f)
                MutateStats(ent, true, random);
        }
        else if (tray.Comp != null && tray.Comp.PestLevel > 5f)
        {
            _holder.AdjustsHealth(ent.Owner, -5f);
        }
    }
}
