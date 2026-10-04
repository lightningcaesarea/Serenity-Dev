using System.Diagnostics.CodeAnalysis;
using Content.Server.Administration.Logs;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Research.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Database;
using Content.Shared.Labels.EntitySystems;
using Content.Shared.Research.Components;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Serenity.Cargo;

/// <summary>
/// Pays a sold bounty's <see cref="Content.Shared.Cargo.Prototypes.CargoBountyPrototype.ResearchPoints"/> to a
/// research server on the station that owns the bounty. The Federal Bills reward is paid by the cargo system as usual.
/// </summary>
public sealed partial class BountyResearchSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private CargoSystem _cargo = default!;
    [Dependency] private ResearchSystem _research = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Before the cargo system, which removes the bounty from the station's database when it is sold.
        SubscribeLocalEvent<EntitySoldEvent>(OnSold, before: [typeof(CargoSystem)]);
    }

    private void OnSold(ref EntitySoldEvent args)
    {
        foreach (var sold in args.Sold)
        {
            if (!_container.TryGetContainer(sold, LabelSystem.ContainerName, out var labelSlot) ||
                labelSlot.ContainedEntities.Count == 0 ||
                !TryComp<CargoBountyLabelComponent>(labelSlot.ContainedEntities[0], out var label))
                continue;

            if (label.AssociatedStationId is not { } station ||
                !_cargo.TryGetBountyFromId(station, label.Id, out var bounty) ||
                !_proto.Resolve(bounty.Value.Bounty, out var prototype) ||
                prototype.ResearchPoints <= 0 ||
                !_cargo.IsBountyComplete(sold, bounty.Value))
                continue;

            if (!TryGetStationServer(station, out var server))
                continue;

            _research.ModifyServerPoints(server.Value, prototype.ResearchPoints, server.Value.Comp);
            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"Bounty \"{bounty.Value.Bounty}\" (id:{bounty.Value.Id}) paid {prototype.ResearchPoints} research points to {ToPrettyString(server.Value):server}");
        }
    }

    private bool TryGetStationServer(EntityUid station, [NotNullWhen(true)] out Entity<ResearchServerComponent>? server)
    {
        var query = EntityQueryEnumerator<ResearchServerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_station.GetOwningStation(uid) != station)
                continue;

            server = (uid, comp);
            return true;
        }

        server = null;
        return false;
    }
}
