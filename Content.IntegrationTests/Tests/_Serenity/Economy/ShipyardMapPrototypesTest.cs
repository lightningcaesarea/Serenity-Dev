using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared._Serenity.Shipyard.Prototypes;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Serenity.Economy;

/// <summary>
/// A map that uses an entity prototype that does not exist refuses to load, so the ship cannot be bought
/// (the shipyard just says the purchase failed). Every listed vessel's map must only use known prototypes.
/// </summary>
[TestFixture]
public sealed class ShipyardMapPrototypesTest
{
    private static readonly Regex ProtoLine = new(@"^- proto: (\S+)", RegexOptions.Multiline);

    [Test]
    public async Task VesselMapsOnlyUseKnownPrototypes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();
        var res = pair.Server.ResolveDependency<IResourceManager>();

        var problems = new List<string>();
        foreach (var vessel in proto.EnumeratePrototypes<VesselPrototype>().OrderBy(v => v.ID))
        {
            if (!res.TryContentFileRead(vessel.ShuttlePath, out var stream))
            {
                problems.Add($"{vessel.ID}: map {vessel.ShuttlePath} does not exist");
                continue;
            }

            string text;
            using (stream)
            using (var reader = new StreamReader(stream))
                text = reader.ReadToEnd();

            var missing = ProtoLine.Matches(text)
                .Select(m => m.Groups[1].Value)
                .Where(p => p != "\"\"" && !proto.HasIndex<EntityPrototype>(p))
                .Distinct()
                .OrderBy(p => p);

            foreach (var p in missing)
                problems.Add($"{vessel.ID}: {vessel.ShuttlePath} uses unknown prototype {p}");
        }

        Assert.That(problems, Is.Empty, string.Join("\n", problems));

        await pair.CleanReturnAsync();
    }
}
