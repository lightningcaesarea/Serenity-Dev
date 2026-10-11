using Content.Shared._Serenity.Botany;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Items.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects.Effects.Botany.PlantAttributes;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Serenity.Botany;

/// <summary>
/// Runs the handheld plant analyzer: picks what was scanned, builds the readout on the server and keeps it fresh
/// while the scanned thing stays in range.
/// </summary>
public sealed partial class PlantAnalyzerSystem : EntitySystem
{
    [Dependency] private BotanySystem _botany = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PlantSystem _plant = default!;
    [Dependency] private PlantTraySystem _tray = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    [Dependency] private EntityQuery<PlantComponent> _plantQuery;
    [Dependency] private EntityQuery<PlantHolderComponent> _holderQuery;
    [Dependency] private EntityQuery<PlantTrayComponent> _trayQuery;
    [Dependency] private EntityQuery<SeedComponent> _seedQuery;
    [Dependency] private EntityQuery<ProduceComponent> _produceQuery;

    [SubscribeLocalEvent]
    private void OnAfterInteract(Entity<PlantAnalyzerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target || !args.CanReach)
            return;

        if (!IsScannable(target))
            return;

        args.Handled = true;
        ent.Comp.Target = target;
        ent.Comp.NextUpdate = _timing.CurTime + ent.Comp.UpdateInterval;

        _audio.PlayPvs(ent.Comp.ScanSound, ent);
        _ui.OpenUi(ent.Owner, PlantAnalyzerUiKey.Key, args.User);
        UpdateUi(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<PlantAnalyzerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Target == null || comp.NextUpdate > _timing.CurTime)
                continue;

            comp.NextUpdate = _timing.CurTime + comp.UpdateInterval;

            if (!_ui.IsUiOpen(uid, PlantAnalyzerUiKey.Key)
                || Deleted(comp.Target)
                || !_transform.InRange(Transform(uid).Coordinates, Transform(comp.Target.Value).Coordinates, comp.MaxScanDistance))
            {
                comp.Target = null;
                _ui.CloseUi(uid, PlantAnalyzerUiKey.Key);
                continue;
            }

            UpdateUi((uid, comp));
        }
    }

    private bool IsScannable(EntityUid target)
    {
        return _plantQuery.HasComp(target)
               || _trayQuery.HasComp(target)
               || _seedQuery.HasComp(target)
               || _produceQuery.HasComp(target);
    }

    private void UpdateUi(Entity<PlantAnalyzerComponent> ent)
    {
        if (ent.Comp.Target is not { } target)
            return;

        // What describes the plant: a growing plant is read directly, a seed or produce through its stored snapshot.
        EntityUid? plantUid = null;
        EntityUid? snapshot = null;
        EntProtoId? protoId = null;
        Entity<PlantTrayComponent>? tray = null;

        if (_trayQuery.TryComp(target, out var targetTray))
        {
            tray = (target, targetTray);
            if (_tray.TryGetPlant((target, targetTray), out var inTray))
                plantUid = inTray;
        }
        else if (_plantQuery.HasComp(target))
        {
            plantUid = target;
            if (_plant.TryGetTray(target, out var parent))
                tray = parent;
        }
        else if (_seedQuery.TryComp(target, out var seed))
        {
            snapshot = seed.PlantData;
            protoId = seed.PlantProtoId;
        }
        else if (_produceQuery.TryComp(target, out var produce))
        {
            snapshot = produce.PlantData;
            protoId = produce.PlantProtoId;
        }

        if (plantUid != null)
        {
            snapshot = plantUid;
            protoId = MetaData(plantUid.Value).EntityPrototype?.ID;
        }

        var soil = new List<PlantAnalyzerReagent>();
        PlantAnalyzerTrayData? trayData = null;
        if (tray is { } trayEnt)
        {
            var soilContents = GetSoil(trayEnt);
            soil = soilContents.ConvertAll(e => new PlantAnalyzerReagent(e.Name, e.Units, 0f));
            trayData = BuildTrayData(trayEnt.Comp, soilContents);
        }

        PlantAnalyzerPlantData? plantData = null;
        var produceList = new List<PlantAnalyzerReagent>();
        var mutatesInto = new List<string>();

        if (_botany.TryGetPlantComponent<PlantComponent>(snapshot, protoId, out var plant)
            && _botany.TryGetPlantComponent<PlantDataComponent>(snapshot, protoId, out var data))
        {
            _botany.TryGetPlantComponent<PlantInstabilityComponent>(snapshot, protoId, out var instability);

            var growing = plantUid != null && _holderQuery.TryComp(plantUid, out _);
            _holderQuery.TryComp(plantUid, out var holder);
            var cycle = (float) (holder?.CycleDelay ?? TimeSpan.FromSeconds(15)).TotalSeconds;

            plantData = new PlantAnalyzerPlantData(
                Loc.GetString(data.Name),
                growing,
                holder?.Dead ?? false,
                holder?.ReadyForHarvest ?? false,
                holder?.Health ?? plant.Endurance,
                plant.Endurance,
                (holder?.Age ?? 0) * cycle,
                plant.Maturation * cycle,
                plant.Production * cycle,
                plant.Lifespan * cycle,
                plant.Yield,
                plant.Potency,
                instability?.Instability ?? 0f);

            produceList = GetProduceChemicals(snapshot, protoId, plant);

            foreach (var species in data.MutationPrototypes)
            {
                if (_botany.TryGetPlantComponent<PlantDataComponent>(null, species, out var other))
                    mutatesInto.Add(Loc.GetString(other.Name));
            }
        }

        var state = new PlantAnalyzerUiState(
            GetNetEntity(target),
            Identity.Name(target, EntityManager),
            trayData,
            plantData,
            soil,
            produceList,
            mutatesInto);

        _ui.SetUiState(ent.Owner, PlantAnalyzerUiKey.Key, state);
    }

    private List<SoilEntry> GetSoil(Entity<PlantTrayComponent> tray)
    {
        var result = new List<SoilEntry>();

        // The tray's own cached reference is not ours to touch, so resolve a fresh one.
        Entity<SolutionComponent>? soilEnt = null;
        if (!_solutions.ResolveSolution(tray.Owner, tray.Comp.SoilSolutionName, ref soilEnt, out var solution))
            return result;

        foreach (var (reagent, quantity) in solution.Contents)
        {
            ProtoMan.TryIndex<ReagentPrototype>(reagent.Prototype, out var proto);
            result.Add(new SoilEntry(proto, proto?.LocalizedName ?? reagent.Prototype, quantity.Float()));
        }

        return result;
    }

    private PlantAnalyzerTrayData BuildTrayData(PlantTrayComponent tray, List<SoilEntry> soil)
    {
        var weedRaisers = new List<string>();
        var pestRaisers = new List<string>();
        var toxinRaisers = new List<string>();

        // Name the soil reagents that are pushing a problem up, so the botanist knows what to flush out.
        foreach (var (proto, name, _) in soil)
        {
            if (proto == null)
                continue;

            foreach (var effect in proto.PlantMetabolisms)
            {
                switch (effect)
                {
                    case PlantAdjustWeeds { Amount: > 0 }:
                        weedRaisers.Add(name);
                        break;
                    case PlantAdjustPests { Amount: > 0 }:
                        pestRaisers.Add(name);
                        break;
                    case PlantAdjustToxins { Amount: > 0 }:
                        toxinRaisers.Add(name);
                        break;
                }
            }
        }

        return new PlantAnalyzerTrayData(
            tray.WaterLevel,
            tray.MaxWaterLevel,
            tray.NutritionLevel,
            tray.MaxNutritionLevel,
            tray.WeedLevel,
            tray.MaxWeedLevel,
            tray.PestLevel,
            tray.MaxPestLevel,
            tray.ToxinLevel,
            tray.MaxToxinLevel,
            tray.WeedGrowthChance,
            weedRaisers,
            pestRaisers,
            toxinRaisers);
    }

    private sealed record SoilEntry(ReagentPrototype? Proto, string Name, float Units);

    private List<PlantAnalyzerReagent> GetProduceChemicals(EntityUid? snapshot, EntProtoId? protoId, PlantComponent plant)
    {
        var result = new List<PlantAnalyzerReagent>();
        if (!_botany.TryGetPlantComponent<PlantChemicalsComponent>(snapshot, protoId, out var chemicals))
            return result;

        var total = 0f;
        var amounts = new List<(string Name, float Units)>();
        foreach (var (reagent, quantity) in chemicals.Chemicals)
        {
            // The same arithmetic the produce uses when it grows.
            var amount = quantity.Min;
            if (quantity.PotencyDivisor > 0 && plant.Potency > 0)
                amount += plant.Potency / quantity.PotencyDivisor;
            amount = FixedPoint2.Clamp(amount, quantity.Min, quantity.Max);

            var name = ProtoMan.TryIndex<ReagentPrototype>(reagent, out var proto) ? proto.LocalizedName : reagent.Id;
            amounts.Add((name, amount.Float()));
            total += amount.Float();
        }

        foreach (var (name, units) in amounts)
        {
            result.Add(new PlantAnalyzerReagent(name, units, total > 0f ? units / total * 100f : 0f));
        }

        return result;
    }
}
