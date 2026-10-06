namespace Content.Shared._Serenity.Flooding;

/// <summary>
///     How a flood looks at each depth. Only the client reads it.
/// </summary>
[RegisterComponent]
public sealed partial class FloodVisualsComponent : Component
{
    /// <summary>
    ///     Opacity of the liquid at each depth, so deeper water hides more of the floor.
    /// </summary>
    [DataField]
    public Dictionary<FloodDepth, float> Alpha = new()
    {
        { FloodDepth.Ankles, 0.45f },
        { FloodDepth.Waist, 0.6f },
        { FloodDepth.Chest, 0.75f },
        { FloodDepth.Submerged, 0.9f },
    };

    /// <summary>
    ///     Opacity used before the depth is known, and for splashes.
    /// </summary>
    [DataField]
    public float DefaultAlpha = 0.75f;
}
