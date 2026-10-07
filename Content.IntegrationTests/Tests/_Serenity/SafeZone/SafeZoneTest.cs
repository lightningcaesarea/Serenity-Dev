using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Serenity.SafeZone;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Serenity.SafeZone;

/// <summary>
/// Players on a safe zone station's main grid are pacified, and only the zone's own pacification is lifted when
/// they leave.
/// </summary>
[TestFixture]
public sealed class SafeZoneTest : GameTest
{
    private const string Map = "SerenitySafeZoneTestMap";
    private const string ExemptMap = "SerenitySafeZoneExemptTestMap";
    private static readonly ProtoId<JobPrototype> Assistant = "Assistant";

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
        - type: SafeZone
        - type: StationJobs
          availableJobs:
            {Assistant}: [ 1, 1 ]

- type: gameMap
  id: {ExemptMap}
  mapName: {ExemptMap}
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
        - type: StationNameSetup
          mapNameTemplate: ""Empty""
        - type: SafeZone
          exemptJobs:
          - {Assistant}
        - type: StationJobs
          availableJobs:
            {Assistant}: [ 1, 1 ]
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
    [SidedDependency(Side.Server)] private readonly SharedTransformSystem _transform = default!;

    private async Task<EntityUid> SpawnAsAssistant(string map = Map)
    {
        Server.CfgMan.SetCVar(CCVars.GameMap, map);
        await Pair.SetJobPreferences([Assistant]);
        await Pair.SetJobPriorities((Assistant, JobPriority.High));
        _ticker.ToggleReadyAll(true);
        await Server.WaitPost(() => _ticker.StartRound());
        await RunTicksSync(10);

        EntityUid body = default;
        await Server.WaitAssertion(() => body = ServerSession!.AttachedEntity!.Value);
        return body;
    }

    private async Task LeaveTheGrid(EntityUid body)
    {
        await Server.WaitPost(() =>
        {
            var mapId = SEntMan.GetComponent<TransformComponent>(body).MapID;
            _transform.SetMapCoordinates(body, new MapCoordinates(new Vector2(500f, 500f), mapId));
        });
        await RunSeconds(2);
    }

    [Test]
    public async Task PacifiedOnTheGridAndFreedOffIt()
    {
        var body = await SpawnAsAssistant();
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<PacifiedComponent>(body), "pacified on the station grid");
            Assert.That(SEntMan.HasComponent<SafeZonePacifiedComponent>(body), "marked as pacified by the zone");
        });

        await LeaveTheGrid(body);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<PacifiedComponent>(body), Is.False, "free to fight off the grid");
            Assert.That(SEntMan.HasComponent<SafeZonePacifiedComponent>(body), Is.False);
        });

        await Server.WaitPost(() => _ticker.RestartRound());
    }

    [Test]
    public async Task OtherPacificationIsKept()
    {
        var body = await SpawnAsAssistant();
        await Server.WaitPost(() => SEntMan.EnsureComponent<PacifiedComponent>(body));
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<SafeZonePacifiedComponent>(body), Is.False, "already pacified, not by the zone"));

        await LeaveTheGrid(body);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<PacifiedComponent>(body), "the zone never lifts pacification it didn't add"));

        await Server.WaitPost(() => _ticker.RestartRound());
    }

    [Test]
    public async Task ExemptJobsAreNotPacified()
    {
        var body = await SpawnAsAssistant(ExemptMap);
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<PacifiedComponent>(body), Is.False, "exempt jobs (security) can still fight"));

        await Server.WaitPost(() => _ticker.RestartRound());
    }
}
