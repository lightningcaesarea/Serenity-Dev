using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Serenity.Intimacy;

/// <summary>
/// The client only draws an anatomy marking when the species' base sprites list its layer (otherwise the
/// marking is hidden) and the body's sprite has a layer mapped for it (otherwise it is skipped outright).
/// Checks both for every round-start species, so a new species can't silently lose them.
/// </summary>
[TestFixture]
public sealed class AnatomyLayersTest : InteractionTest
{
    private static readonly HumanoidVisualLayers[] AnatomyLayers =
    {
        HumanoidVisualLayers.Breasts,
        HumanoidVisualLayers.Penis,
        HumanoidVisualLayers.Testicles,
        HumanoidVisualLayers.Vagina,
        HumanoidVisualLayers.Butt,
        HumanoidVisualLayers.Belly,
    };

    [Test]
    public async Task EveryRoundStartSpeciesCanShowAnatomy()
    {
        var sprites = CEntMan.System<SpriteSystem>();

        foreach (var species in ProtoMan.EnumeratePrototypes<SpeciesPrototype>())
        {
            if (!species.RoundStart)
                continue;

            var baseSprites = ProtoMan.Index<HumanoidSpeciesBaseSpritesPrototype>(species.SpriteSet);
            foreach (var layer in AnatomyLayers)
            {
                Assert.That(baseSprites.Sprites.ContainsKey(layer),
                    $"{species.ID}: {baseSprites.ID} has no {layer} entry, so {layer} markings stay hidden");
            }

            var body = await Spawn(species.Prototype);
            var cBody = ToClient(body);
            var sprite = CEntMan.GetComponent<SpriteComponent>(cBody);

            await Client.WaitAssertion(() =>
            {
                foreach (var layer in AnatomyLayers)
                {
                    Assert.That(sprites.LayerMapTryGet((cBody, sprite), layer, out _, false),
                        $"{species.ID}: {species.Prototype} has no {layer} sprite layer, so {layer} markings are never drawn");
                }
            });

            await Delete(body);
        }
    }
}
