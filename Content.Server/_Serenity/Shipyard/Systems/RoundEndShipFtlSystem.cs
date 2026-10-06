using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Serenity.CCVar;
using Content.Shared._Serenity.Shipyard.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Configuration;

namespace Content.Server._Serenity.Shipyard.Systems;

/// <summary>
/// When the round ends, every player-owned ship (a grid carrying a <see cref="ShuttleDeedComponent"/>) FTLs to
/// CentComm, crew and cargo included, so ships leave the sector with the shift instead of being deleted with the
/// station. Round-to-round persistence of ships is a separate, later feature.
/// </summary>
public sealed partial class RoundEndShipFtlSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        if (ev.New != GameRunLevel.PostRound || !_cfg.GetCVar(SerenityCCVars.RoundEndShipFtl))
            return;

        EntityUid? centcommGrid = null;
        EntityUid? centcommMap = null;
        var centcommQuery = EntityQueryEnumerator<StationCentcommComponent>();
        while (centcommQuery.MoveNext(out var centcomm))
        {
            if (centcomm.Entity is not { } grid || centcomm.MapEntity is not { } map)
                continue;

            centcommGrid = grid;
            centcommMap = map;
            break;
        }

        if (centcommGrid == null)
        {
            Log.Warning("Round ended but there is no CentComm grid; owned ships stay where they are.");
            return;
        }

        var startup = _cfg.GetCVar(SerenityCCVars.RoundEndShipFtlStartup);
        var travel = _cfg.GetCVar(SerenityCCVars.RoundEndShipFtlTravel);

        var ships = new List<(EntityUid Uid, ShuttleComponent Shuttle)>();
        var query = EntityQueryEnumerator<ShuttleDeedComponent, ShuttleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var shuttle, out var xform))
        {
            if (xform.MapUid == centcommMap || HasComp<FTLComponent>(uid))
                continue;

            ships.Add((uid, shuttle));
        }

        foreach (var (uid, shuttle) in ships)
        {
            _shuttle.FTLToDock(uid, shuttle, centcommGrid.Value, startup, travel);
        }

        if (ships.Count > 0)
            Log.Info($"Sent {ships.Count} owned ship(s) to CentComm at round end.");
    }
}
