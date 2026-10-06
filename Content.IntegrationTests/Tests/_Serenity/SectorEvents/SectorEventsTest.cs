using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Serenity.Cryo;
using Content.Server._Serenity.SectorEvents;
using Content.Server.GameTicking;
using Content.Server.Ghost;
using Content.Server.Shuttles.Components;
using Robust.Shared.Maths;
using Content.Server.Station.Systems;
using Content.Shared._Serenity.CCVar;
using Content.Shared._Serenity.Shipyard.Components;
using Content.Shared._Starlight.CryoTeleportation;
using Content.Shared.Bed.Cryostorage;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Serenity.SectorEvents;

/// <summary>
/// The sector event grid, the Frontier-style cryo return and the round-end ship FTL, all on one small station.
/// </summary>
[TestFixture]
public sealed class SectorEventsTest : GameTest
{
    private const string Map = "SerenitySectorTestMap";
    private static readonly ProtoId<JobPrototype> Passenger = "Assistant";
    private static readonly EntProtoId CryoPod = "CryogenicSleepUnit";
    private static readonly EntProtoId SmallVault = "SectorEventVaultSmall";

    // YAML indentation, not C#.
    // editorconfig-checker-disable
    [TestPrototypes]
    private static readonly string Prototypes = $@"
- type: gameMap
  id: {Map}
  mapName: {Map}
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
        - type: StationNameSetup
          mapNameTemplate: ""Empty""
        - type: StationJobs
          availableJobs:
            {Passenger}: [ -1, -1 ]
";
    // editorconfig-checker-enable

    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
        DummyTicker = false,
        Connected = true,
        InLobby = true,
    };

    [SidedDependency(Side.Server)] private readonly GameTicker _ticker = default!;
    [SidedDependency(Side.Server)] private readonly StationSystem _station = default!;
    [SidedDependency(Side.Server)] private readonly GhostSystem _ghost = default!;
    [SidedDependency(Side.Server)] private readonly SharedMindSystem _mind = default!;
    [SidedDependency(Side.Server)] private readonly ITileDefinitionManager _tiles = default!;

    private async Task<(EntityUid Body, EntityUid Station, EntityUid Grid)> StartRound()
    {
        Server.CfgMan.SetCVar(CCVars.GameMap, Map);
        await Pair.SetJobPreferences([Passenger]);
        await Pair.SetJobPriorities((Passenger, JobPriority.High));
        _ticker.ToggleReadyAll(true);
        await Server.WaitPost(() => _ticker.StartRound());
        await RunTicksSync(10);

        EntityUid body = default, station = default, grid = default;
        await Server.WaitAssertion(() =>
        {
            body = ServerSession!.AttachedEntity!.Value;
            station = _station.GetOwningStation(body)!.Value;
            grid = _station.GetLargestGrid(station)!.Value;
        });

        return (body, station, grid);
    }

    /// <summary>
    /// A sector event puts its grid in space near the station and removes it again when it ends.
    /// </summary>
    [Test]
    public async Task EventGridAppearsAndLeaves()
    {
        await StartRound();

        EntityUid rule = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(_ticker.StartGameRule(SmallVault.Id, out rule));
        });
        // Station events start after their random 10-20s delay.
        await RunSeconds(25);

        EntityUid eventGrid = default;
        await Server.WaitAssertion(() =>
        {
            var comp = SComp<SectorGridEventRuleComponent>(rule);
            Assert.That(comp.Grids, Has.Count.EqualTo(1));
            eventGrid = comp.Grids[0];
            Assert.That(SEntMan.HasComponent<MapGridComponent>(eventGrid));
            Assert.That(SEntMan.HasComponent<ShuttleComponent>(eventGrid), Is.False, "event grids are not flyable");
            Assert.That(SEntMan.HasComponent<IFFComponent>(eventGrid));
            _ticker.EndGameRule(rule);
        });
        await RunTicksSync(5);

        await Server.WaitAssertion(() => Assert.That(SEntMan.Deleted(eventGrid), "grid should be gone after the event"));
        await Server.WaitPost(() => _ticker.RestartRound());
    }

    /// <summary>
    /// A cryo-stored body can be woken again by its owner's ghost.
    /// </summary>
    [Test]
    public async Task CryoStoredBodyWakesOnReturn()
    {
        var (body, station, grid) = await StartRound();
        EntityUid pod = default, mindId = default;

        await Server.WaitAssertion(() =>
        {
            SComp<StationCryoTeleportationComponent>(station).TransferDelay = TimeSpan.Zero;
            pod = SSpawnAtPosition(CryoPod, new EntityCoordinates(grid, -0.5f, -0.5f));
            var cryo = SComp<CryostorageComponent>(pod);
            cryo.GracePeriod = TimeSpan.Zero;
            cryo.NoMindGracePeriod = TimeSpan.Zero;

            Assert.That(_mind.TryGetMind(body, out mindId, out _));
            Assert.That(_ghost.OnGhostAttempt(mindId, true, viaCommand: true));
        });

        // Starlight's auto-cryo parks the abandoned body in the pod's paused map.
        await RunSeconds(8);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CryoReturnComponent>(body), "stored body should be wakeable");
            Assert.That(SEntMan.GetComponent<TransformComponent>(body).MapUid,
                Is.Not.EqualTo(SEntMan.GetComponent<TransformComponent>(pod).MapUid), "body is parked");
            Assert.That(SEntMan.System<CryoReturnSystem>().TryWake(ServerSession!, out var error), error);
        });
        await RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(body).MapUid,
                Is.EqualTo(SEntMan.GetComponent<TransformComponent>(pod).MapUid), "body is back in the pod's map");
            Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(body));
        });

        await Server.WaitPost(() => _ticker.RestartRound());
    }

    /// <summary>
    /// At round end an owned ship FTLs to CentComm.
    /// </summary>
    [Test]
    public async Task OwnedShipLeavesForCentcommAtRoundEnd()
    {
        // Tests turn the emergency shuttle (and with it CentComm) off by default.
        Server.CfgMan.SetCVar(CCVars.EmergencyShuttleEnabled, true);
        var (_, station, grid) = await StartRound();
        Server.CfgMan.SetCVar(SerenityCCVars.RoundEndShipFtlStartup, 1f);
        Server.CfgMan.SetCVar(SerenityCCVars.RoundEndShipFtlTravel, 1f);

        EntityUid ship = default;
        MapId stationMap = default;
        await Server.WaitAssertion(() =>
        {
            stationMap = SEntMan.GetComponent<TransformComponent>(grid).MapID;
            var maps = SEntMan.System<SharedMapSystem>();
            var shipGrid = maps.CreateGridEntity(stationMap);
            ship = shipGrid.Owner;
            maps.SetTile(shipGrid, new Vector2i(0, 0), new Tile(_tiles["Plating"].TileId));
            SEntMan.EnsureComponent<ShuttleComponent>(ship);
            SEntMan.EnsureComponent<ShuttleDeedComponent>(ship);
        });

        await Server.WaitPost(() => _ticker.EndRound());
        await RunSeconds(8);

        await Server.WaitAssertion(() =>
        {
            var centcomm = System.Linq.Enumerable.First(SEntMan.EntityQuery<StationCentcommComponent>());
            Assert.That(centcomm.MapEntity, Is.Not.Null, "test station has a CentComm map");
            Assert.That(SEntMan.GetComponent<TransformComponent>(ship).MapUid, Is.EqualTo(centcomm.MapEntity));
        });
    }
}
