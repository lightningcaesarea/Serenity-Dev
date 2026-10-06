// Design inspired by Frontier Station 14's MIT-era NfAdventureRuleSystem (5be37d18c2, 2024-07-01),
// which hardcoded 13 map loads inline. Their later data-driven `pointOfInterest` prototype postdates
// the licence cutoff (AGPL), so this is an original prototype schema serving the same purpose:
// content authors add a POI by writing a YAML file, not by editing a C# system.

using Content.Shared.Maps;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Serenity.PointsOfInterest;

/// <summary>
/// A grid loaded onto the default map at round start by <c>PoiSpawnerSystem</c>: a destination worth
/// flying to. Distinct from the shipyard (player-owned, purchased) and public transit (fixed loop stops).
/// </summary>
[Prototype]
public sealed partial class SerenityPointOfInterestPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public string Name = string.Empty;

    /// <summary>
    /// Disabled POIs are defined (and their maps kept valid) but never spawned.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Grid file to load, e.g. <c>/Maps/_Serenity/POI/cargodepot.yml</c>.
    /// </summary>
    [DataField(required: true)]
    public ResPath GridPath = default!;

    /// <summary>
    /// Closest the POI may spawn from the map origin, in tiles.
    /// </summary>
    [DataField]
    public float MinimumDistance = 1000f;

    /// <summary>
    /// Farthest the POI may spawn from the map origin, in tiles.
    /// </summary>
    [DataField]
    public float MaximumDistance = 5000f;

    /// <summary>
    /// Radar blip colour.
    /// </summary>
    [DataField]
    public Color IffColor = Color.White;

    /// <summary>
    /// Hides the radar label (but not the blip). For POIs meant to be found rather than advertised.
    /// </summary>
    [DataField]
    public bool HideLabel;

    /// <summary>
    /// If set, the loaded grid becomes its own station using this <see cref="GameMapPrototype"/>'s
    /// station config, so late-joiners can spawn directly on it. The map's own ID is used as the key
    /// into <see cref="GameMapPrototype.Stations"/>, matching how vessel/shipyard station init works.
    /// </summary>
    [DataField]
    public ProtoId<GameMapPrototype>? StationMap;

    /// <summary>
    /// Reserved for a future round-size-based selection pass. Every enabled POI spawns every round
    /// today, so this has no effect yet.
    /// </summary>
    [DataField]
    public float Weight = 1f;
}
