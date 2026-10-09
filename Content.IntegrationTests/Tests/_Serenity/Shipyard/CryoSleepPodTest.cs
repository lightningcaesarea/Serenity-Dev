using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Serenity.Shipyard;

[TestFixture]
public sealed class CryoSleepPodTest
{
    /// <summary>
    /// The shipyard cryo sleep pod is a despawn-and-store sleep unit, not the medical cryo tube.
    /// </summary>
    [Test]
    public async Task CryoSleepPodIsASleepUnit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var proto = protoMan.Index<EntityPrototype>("MachineCryoSleepPod");
            Assert.Multiple(() =>
            {
                Assert.That(proto.Components.ContainsKey("Cryostorage"), "should be a cryostorage sleep unit");
                Assert.That(proto.Components.ContainsKey("CryoPod"), Is.False, "must not be a medical cryo pod");
            });
        });

        await pair.CleanReturnAsync();
    }
}
