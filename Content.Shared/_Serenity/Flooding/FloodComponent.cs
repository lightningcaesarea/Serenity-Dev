using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._Serenity.Flooding;

/// <summary>
///     Standing liquid that fills a whole tile, deeper than a puddle can hold.
///     Floods flow into lower neighbouring tiles until the level evens out, are stopped by anything airtight,
///     pour out into space, and fall back into an ordinary puddle once they get too shallow.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FloodComponent : Component
{
    [DataField]
    public string Solution = "flood";

    /// <summary>
    ///     How deep the liquid on this tile is, worked out on the server from its volume.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FloodDepth Depth = FloodDepth.Ankles;

    /// <summary>
    ///     Walk and sprint speed multiplier for anything wading through each depth.
    /// </summary>
    [DataField]
    public Dictionary<FloodDepth, float> SpeedModifiers = new()
    {
        { FloodDepth.Ankles, 0.85f },
        { FloodDepth.Waist, 0.65f },
        { FloodDepth.Chest, 0.5f },
        { FloodDepth.Submerged, 0.4f },
    };

    /// <summary>
    ///     Volume the last time the solution changed, so the server can tell rising water from falling water.
    /// </summary>
    [ViewVariables]
    public FixedPoint2 LastVolume;

    [ViewVariables]
    public Entity<SolutionComponent>? SolutionEntity;
}
