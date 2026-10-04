using System.Collections.Generic;
using System.Linq;
using Content.Server._Serenity.Xenobiology;
using Content.Shared._Serenity.Xenobiology;
using Content.Shared._Starlight.Xenobiology;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Serenity.Xenobiology;

[TestFixture]
public sealed class SlimeResearchTest
{
    private static readonly ProtoId<TagPrototype>[] Tiers =
    [
        "SlimeExtractTier0", "SlimeExtractTier1", "SlimeExtractTier2", "SlimeExtractTier3", "SlimeExtractTier4",
    ];

    /// <summary>
    /// Every extract a slime can drop has exactly one tier tag, the processor pays for every tier, and rarer
    /// tiers pay more.
    /// </summary>
    [Test]
    public async Task EveryExtractHasOnePaidTier()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ProtoMan;
        var tags = entMan.System<TagSystem>();
        var research = entMan.System<SlimeResearchSystem>();
        var mapData = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var extracts = new HashSet<EntProtoId>();
            foreach (var slime in proto.EnumeratePrototypes<EntityPrototype>())
            {
                if (slime.Abstract || !slime.TryGetComponent<SlimeComponent>(out var comp, entMan.ComponentFactory))
                    continue;

                if (comp.Extract != default)
                    extracts.Add(comp.Extract);
            }

            var processor = entMan.SpawnEntity("SlimeProcessor", mapData.GridCoords);
            var payout = entMan.GetComponent<SlimeProcessorResearchComponent>(processor);

            Assert.Multiple(() =>
            {
                Assert.That(extracts, Is.Not.Empty, "no slime drops an extract");

                foreach (var extractId in extracts)
                {
                    var extract = entMan.SpawnEntity(extractId, mapData.GridCoords);
                    var count = Tiers.Count(t => tags.HasTag(extract, t));
                    Assert.That(count, Is.EqualTo(1), $"{extractId} should have exactly one slime extract tier tag");
                    Assert.That(research.TryGetTierPoints(payout, extract, out _), $"the slime processor pays nothing for {extractId}");
                    entMan.DeleteEntity(extract);
                }

                for (var i = 0; i < Tiers.Length; i++)
                {
                    Assert.That(payout.TierPoints.ContainsKey(Tiers[i]), $"the slime processor has no value for {Tiers[i]}");
                    if (i > 0)
                        Assert.That(payout.TierPoints.GetValueOrDefault(Tiers[i]), Is.GreaterThan(payout.TierPoints.GetValueOrDefault(Tiers[i - 1])), $"{Tiers[i]} should pay more than {Tiers[i - 1]}");
                }

                // The discovery extract pays the most and repeats always pay less than the one before.
                var previous = int.MaxValue;
                for (var n = 0; n < 10; n++)
                {
                    var points = SlimeResearchSystem.GetPoints(payout, 1000, n);
                    Assert.That(points, Is.LessThan(previous), $"extract {n + 1} of a colour should pay less than extract {n}");
                    previous = points;
                }
            });

            entMan.DeleteEntity(processor);
        });

        await pair.CleanReturnAsync();
    }
}
