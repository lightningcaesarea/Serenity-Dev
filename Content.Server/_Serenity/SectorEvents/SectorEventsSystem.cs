using Content.Server.GameTicking;
using Content.Shared._Serenity.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._Serenity.SectorEvents;

/// <summary>
/// Starts the sector event scheduler when a round begins, whatever game preset is running.
/// </summary>
public sealed partial class SectorEventsSystem : EntitySystem
{
    private static readonly EntProtoId Scheduler = "SectorEventScheduler";

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private GameTicker _ticker = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        if (ev.New != GameRunLevel.InRound || !_cfg.GetCVar(SerenityCCVars.SectorEventsEnabled))
            return;

        _ticker.StartGameRule(Scheduler.Id);
    }
}
