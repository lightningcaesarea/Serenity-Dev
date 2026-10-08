using Content.Shared._Serenity.Botany;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Content.Shared.Verbs;

namespace Content.Server._Serenity.Botany;

/// <summary>
/// Small quality-of-life behaviour for hydroponics trays: the tray is named after what grows in it, its soil
/// chemicals can be emptied out, and a plant dug up with a shovel composts back into the soil.
/// </summary>
public sealed partial class PlantTrayExtrasSystem : EntitySystem
{
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PlantTraySystem _tray = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    [Dependency] private EntityQuery<PlantDataComponent> _dataQuery;
    [Dependency] private EntityQuery<PlantTrayComponent> _trayQuery;

    /// <summary>
    /// How much of a plant's produce chemicals returns to the soil when it is dug up.
    /// </summary>
    private const float CompostFraction = 0.25f;

    /// <summary>
    /// The most compost one plant can give back, in units.
    /// </summary>
    private static readonly FixedPoint2 CompostCap = FixedPoint2.New(15);

    [SubscribeLocalEvent]
    private void OnPlantParentChanged(Entity<PlantComponent> ent, ref EntParentChangedMessage args)
    {
        if (args.OldParent is { } oldParent)
            ClearName(oldParent, ent.Owner);

        if (_trayQuery.HasComp(args.Transform.ParentUid))
            SetName(args.Transform.ParentUid, ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnPlantShutdown(Entity<PlantComponent> ent, ref ComponentShutdown args)
    {
        ClearName(Transform(ent).ParentUid, ent.Owner);
    }

    private void SetName(EntityUid trayUid, EntityUid plant)
    {
        if (!_trayQuery.HasComp(trayUid) || TerminatingOrDeleted(trayUid) || !_dataQuery.TryComp(plant, out var data))
            return;

        var label = EnsureComp<PlantTrayLabelComponent>(trayUid);
        label.BaseName ??= MetaData(trayUid).EntityName;
        _meta.SetEntityName(trayUid, Loc.GetString("plant-tray-named", ("tray", label.BaseName), ("plant", Loc.GetString(data.Name))));
    }

    private void ClearName(EntityUid trayUid, EntityUid removedPlant)
    {
        if (!_trayQuery.TryComp(trayUid, out var tray) || TerminatingOrDeleted(trayUid))
            return;

        // A species change plants the new plant before the old one goes: leave the tray's name alone then.
        if (tray.PlantEntity != null && tray.PlantEntity != removedPlant && !TerminatingOrDeleted(tray.PlantEntity.Value))
            return;

        if (!TryComp<PlantTrayLabelComponent>(trayUid, out var label) || label.BaseName == null)
            return;

        _meta.SetEntityName(trayUid, label.BaseName);
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(Entity<PlantTrayComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("plant-tray-empty-chemicals-verb"),
            Priority = -1,
            Act = () => EmptyChemicals(ent, user),
        });
    }

    private void EmptyChemicals(Entity<PlantTrayComponent> tray, EntityUid user)
    {
        Entity<SolutionComponent>? soil = null;
        if (!_solutions.ResolveSolution(tray.Owner, tray.Comp.SoilSolutionName, ref soil, out var solution)
            || solution.Volume <= 0)
        {
            _popup.PopupEntity(Loc.GetString("plant-tray-empty-chemicals-nothing", ("tray", tray.Owner)), tray.Owner, user);
            return;
        }

        _solutions.RemoveAllSolution(soil.Value);
        _popup.PopupEntity(Loc.GetString("plant-tray-empty-chemicals-done", ("tray", tray.Owner)), tray.Owner, user);
    }

    [SubscribeLocalEvent]
    private void OnPlantDugUp(Entity<PlantTrayComponent> ent, ref PlantDugUpEvent args)
    {
        var plantUid = args.Plant;

        if (!TryComp<PlantChemicalsComponent>(plantUid, out var chemicals)
            || !TryComp<PlantComponent>(plantUid, out var plant))
            return;

        Entity<SolutionComponent>? soil = null;
        if (!_solutions.ResolveSolution(ent.Owner, ent.Comp.SoilSolutionName, ref soil, out _))
            return;

        // A fraction of the chemicals the produce would have held goes back into the soil.
        var returned = FixedPoint2.Zero;
        foreach (var (reagent, quantity) in chemicals.Chemicals)
        {
            var amount = quantity.Min;
            if (quantity.PotencyDivisor > 0 && plant.Potency > 0)
                amount += plant.Potency / quantity.PotencyDivisor;
            amount = FixedPoint2.Clamp(amount, quantity.Min, quantity.Max) * CompostFraction;

            if (returned + amount > CompostCap)
                amount = CompostCap - returned;

            if (amount <= FixedPoint2.Zero)
                break;

            if (_solutions.TryAddReagent(soil.Value, reagent, amount, out var accepted))
                returned += accepted;
        }
    }
}
