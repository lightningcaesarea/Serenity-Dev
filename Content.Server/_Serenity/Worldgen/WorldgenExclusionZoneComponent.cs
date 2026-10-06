namespace Content.Server._Serenity.Worldgen;

/// <summary>
/// On a grid: worldgen debris will not be placed within <see cref="Radius"/> tiles of this grid's edge.
/// Used to keep the hub station clear of asteroids.
/// </summary>
[RegisterComponent]
public sealed partial class WorldgenExclusionZoneComponent : Component
{
    /// <summary>
    /// Clear distance, in tiles, measured from the grid's bounding box.
    /// </summary>
    [DataField]
    public float Radius = 300f;
}
