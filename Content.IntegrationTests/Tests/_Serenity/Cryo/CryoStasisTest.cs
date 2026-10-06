using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.GameTicking;
using Content.Server.Ghost;
using Content.Server.Station.Systems;
using Content.Shared._Serenity.CCVar;
using Content.Shared.Bed.Cryostorage;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Serenity.Cryo;

/// <summary>
/// A sleeper holds their body and job slot for an hour; ghosting out of the pod sends them to stasis at once.
/// </summary>
[TestFixture]
public sealed class CryoStasisTest : GameTest
{
    private const string Map = "SerenityCryoStasisTestMap";
    private static readonly ProtoId<JobPrototype> Mime = "Mime";
    private static readonly EntProtoId CryoPod = "CryogenicSleepUnit";

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
            {Mime}: [ 1, 1 ]
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
    [SidedDependency(Side.Server)] private readonly StationJobsSystem _stationJobs = default!;
    [SidedDependency(Side.Server)] private readonly GhostSystem _ghost = default!;
    [SidedDependency(Side.Server)] private readonly SharedMindSystem _mind = default!;
    [SidedDependency(Side.Server)] private readonly SharedContainerSystem _container = default!;
    [SidedDependency(Side.Server)] private readonly IGameTiming _timing = default!;

    private void AssertSlots(EntityUid station, int expected)
    {
        Assert.That(_stationJobs.TryGetJobSlot(station, Mime, out var slots));
        Assert.That(slots, Is.EqualTo(expected), $"Open {Mime} slots");
    }

    /// <summary>
    /// Puts the player's body to sleep in a pod: grace period is an hour, the slot stays taken.
    /// </summary>
    private async Task<(EntityUid Body, EntityUid Station, EntityUid Pod)> SleepInPod()
    {
        Server.CfgMan.SetCVar(CCVars.GameMap, Map);
        await Pair.SetJobPreferences([Mime]);
        await Pair.SetJobPriorities((Mime, JobPriority.High));
        _ticker.ToggleReadyAll(true);
        await Server.WaitPost(() => _ticker.StartRound());
        await RunTicksSync(10);

        EntityUid body = default, station = default, pod = default;
        await Server.WaitAssertion(() =>
        {
            body = ServerSession!.AttachedEntity!.Value;
            station = _station.GetOwningStation(body)!.Value;
            var grid = _station.GetLargestGrid(station)!.Value;
            AssertSlots(station, 0);

            pod = SSpawnAtPosition(CryoPod, new EntityCoordinates(grid, -0.5f, -0.5f));
            Assert.That(_container.Insert(body, _container.GetContainer(pod, SComp<CryostorageComponent>(pod).ContainerId)));
        });
        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var contained = SComp<CryostorageContainedComponent>(body);
            Assert.That(contained.GracePeriodEndTime, Is.Not.Null);
            Assert.That(contained.GracePeriodEndTime!.Value - _timing.CurTime, Is.GreaterThan(TimeSpan.FromMinutes(55)),
                "a sleeper is held for about an hour");
            AssertSlots(station, 0);
        });

        return (body, station, pod);
    }

    /// <summary>
    /// A sleeper who has not left keeps everything; the body is still in the pod's map and the slot is taken.
    /// </summary>
    [Test]
    public async Task SleeperKeepsBodyAndSlot()
    {
        var (body, station, pod) = await SleepInPod();
        await RunSeconds(60);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(body).MapUid,
                Is.EqualTo(SEntMan.GetComponent<TransformComponent>(pod).MapUid), "still in the pod");
            AssertSlots(station, 0);
        });

        await Server.WaitPost(() => _ticker.RestartRound());
    }

    /// <summary>
    /// A body that has been in stasis long enough is removed.
    /// </summary>
    [Test]
    public async Task StasisBodiesAreRemovedAfterTheirTime()
    {
        Server.CfgMan.SetCVar(SerenityCCVars.CryoStasisLifetime, 0.1f); // six seconds
        var (body, _, _) = await SleepInPod();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_mind.TryGetMind(body, out var mindId, out _));
            Assert.That(_ghost.OnGhostAttempt(mindId, true, viaCommand: true));
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() => Assert.That(SEntMan.Deleted(body), Is.False, "still in stasis, not yet removed"));
        await RunSeconds(10);
        await Server.WaitAssertion(() => Assert.That(SEntMan.Deleted(body), "removed after its time in stasis"));

        await Server.WaitPost(() => _ticker.RestartRound());
    }

    /// <summary>
    /// Ghosting while asleep in the pod puts the character in stasis straight away and opens the job slot.
    /// </summary>
    [Test]
    public async Task GhostingFromThePodSendsToStasisAtOnce()
    {
        var (body, station, pod) = await SleepInPod();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_mind.TryGetMind(body, out var mindId, out _));
            Assert.That(_ghost.OnGhostAttempt(mindId, true, viaCommand: true));
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(body).MapUid,
                Is.Not.EqualTo(SEntMan.GetComponent<TransformComponent>(pod).MapUid), "in stasis");
            AssertSlots(station, 1);
        });

        await Server.WaitPost(() => _ticker.RestartRound());
    }
}
