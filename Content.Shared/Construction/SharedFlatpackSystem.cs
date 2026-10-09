using System.Diagnostics.CodeAnalysis;
using Content.Shared.Construction.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Materials;
using Content.Shared.Tag;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
#region Starlight
using System.Linq;
#endregion

namespace Content.Shared.Construction;

public abstract partial class SharedFlatpackSystem : EntitySystem
{
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] protected IPrototypeManager PrototypeManager = default!;
    [Dependency] protected SharedAppearanceSystem Appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private EntityLookupSystem _entityLookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] protected MachinePartSystem MachinePart = default!;
    [Dependency] protected SharedMaterialStorageSystem MaterialStorage = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private TagSystem _tag = default!;

    // Starlight start - Define tag literals
    private static readonly ProtoId<TagPrototype> _flatpackBlacklistTag = "FlatpackBlacklist";
    private static readonly ProtoId<TagPrototype> _tableTag = "Table";

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<FlatpackComponent, InteractUsingEvent>(OnFlatpackInteractUsing);
        SubscribeLocalEvent<FlatpackComponent, ExaminedEvent>(OnFlatpackExamined);
        SubscribeLocalEvent<FlatpackComponent, GetVerbsEvent<AlternativeVerb>>(OnFlatpackGetVerbs);

        SubscribeLocalEvent<FlatpackCreatorComponent, ItemSlotInsertAttemptEvent>(OnInsertAttempt);
    }

    private void OnInsertAttempt(Entity<FlatpackCreatorComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.Slot.ID != ent.Comp.SlotId || args.Cancelled)
            return;

        // Starlight - FlatpackBlacklist Tag
        if (_tag.HasTag(args.Item, _flatpackBlacklistTag.Id))
        {
            args.Cancelled = true;
            return;
        }

        if (HasComp<MachineBoardComponent>(args.Item))
            return;

        if (TryComp<ComputerBoardComponent>(args.Item, out var computer) && computer.Prototype != null)
            return;

        args.Cancelled = true;
    }

    private void OnFlatpackInteractUsing(Entity<FlatpackComponent> ent, ref InteractUsingEvent args)
    {
        if (!_tool.HasQuality(args.Used, ent.Comp.QualityNeeded) || _container.IsEntityInContainer(ent))
            return;

        if (!HasComp<MapGridComponent>(Transform(ent).GridUid))
            return;

        args.Handled = true;
        TryUnpack(ent, args.User, args.Used);
    }

    // Serenity: tool-free unpacking through the Alt-click menu.
    private void OnFlatpackGetVerbs(Entity<FlatpackComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || _container.IsEntityInContainer(ent))
            return;

        if (!HasComp<MapGridComponent>(Transform(ent).GridUid))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("flatpack-verb-unpack"),
            Priority = 1,
            Act = () => TryUnpack(ent, user, user),
        });
    }

    /// <summary>
    /// Unpacks the flatpack on its current tile. Shared by the tool interaction and the Alt-click verb.
    /// </summary>
    /// <param name="soundSource">Entity the unpack sound is played from.</param>
    private void TryUnpack(Entity<FlatpackComponent> ent, EntityUid user, EntityUid soundSource)
    {
        var (uid, comp) = ent;
        var xform = Transform(ent);

        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        if (comp.Entity == null && comp.RandomEntities == null)
        {
            Log.Error($"No entity prototype present for flatpack {ToPrettyString(ent)}.");

            if (_net.IsServer)
                QueueDel(ent);
            return;
        }

        var buildPos = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        var coords = _map.ToCenterCoordinates(grid, buildPos);

        // TODO FLATPACK
        // make it ignore ghosts
        // Starlight-start
        if (_entityLookup.GetEntitiesIntersecting(coords, LookupFlags.Dynamic | LookupFlags.Static)
            .Any(entity => entity != uid && (!_tag.HasTag(entity, _tableTag.Id) || !ent.Comp.AllowUnpackOnTables)))
        // Starlight-end
        {
            // this popup is on the server because the predicts on the intersection is crazy
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("flatpack-unpack-no-room"), uid, user);
            return;
        }

        if (comp.RandomEntities != null)
            comp.Entity = _random.Pick(comp.RandomEntities);

        if (_net.IsServer)
        {
            var spawn = Spawn(comp.Entity, _map.GridTileToLocal(grid, gridComp, buildPos));
            _adminLogger.Add(LogType.Construction,
                LogImpact.Low,
                $"{ToPrettyString(user):player} unpacked {ToPrettyString(spawn):entity} at {xform.Coordinates} from {ToPrettyString(uid):entity}");
            QueueDel(uid);
        }

        _audio.PlayPredicted(comp.UnpackSound, soundSource, user);
    }

    private void OnFlatpackExamined(Entity<FlatpackComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;
        args.PushMarkup(Loc.GetString("flatpack-examine"));
    }

    protected void SetupFlatpack(Entity<FlatpackComponent?> ent, EntProtoId proto, EntityUid board)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.Entity = proto;
        var machinePrototype = PrototypeManager.Index<EntityPrototype>(proto);

        var meta = MetaData(ent);
        _metaData.SetEntityName(ent, Loc.GetString("flatpack-entity-name", ("name", machinePrototype.Name)), meta);
        _metaData.SetEntityDescription(ent, Loc.GetString("flatpack-entity-description", ("name", machinePrototype.Name)), meta);

        Dirty(ent, meta);
        Appearance.SetData(ent, FlatpackVisuals.Machine, MetaData(board).EntityPrototype?.ID ?? string.Empty);
    }

    /// <summary>
    /// Returns the prototype from a board that the flatpacker will create.
    /// </summary>
    public bool TryGetFlatpackResultPrototype(EntityUid board, [NotNullWhen(true)] out EntProtoId? prototype)
    {
        prototype = null;

        if (TryComp<MachineBoardComponent>(board, out var machine))
            prototype = machine.Prototype;
        else if (TryComp<ComputerBoardComponent>(board, out var computer))
            prototype = computer.Prototype;
        return prototype is not null;
    }

    /// <summary>
    /// Tries to get the cost to produce an item, fails if unable to produce it.
    /// </summary>
    /// <param name="entity">The flatpacking machine</param>
    /// <param name="machineBoard">The machine board to pack. If null, this implies we are packing a computer board</param>
    /// <param name="cost">Cost to produce</param>
    public bool TryGetFlatpackCreationCost(Entity<FlatpackCreatorComponent> entity, EntityUid machineBoard, out Dictionary<string, int> cost)
    {
        cost = new();
        Dictionary<ProtoId<MaterialPrototype>, int> baseCost;
        if (TryComp<MachineBoardComponent>(machineBoard, out var machineBoardComp))
        {
            if (!MachinePart.TryGetMachineBoardMaterialCost((machineBoard, machineBoardComp), out cost, -1))
                return false;
            baseCost = entity.Comp.BaseMachineCost;
        }
        else
            baseCost = entity.Comp.BaseComputerCost;

        foreach (var (mat, amount) in baseCost)
        {
            cost.TryAdd(mat, 0);
            cost[mat] -= amount;
        }

        return true;
    }
}
