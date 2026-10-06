namespace Content.Shared._Serenity.Flooding;

/// <summary>
///     Sprite state used for each flood depth. Only the client reads it.
/// </summary>
[RegisterComponent]
public sealed partial class FloodVisualsComponent : Component
{
    [DataField]
    public Dictionary<FloodDepth, string> States = new()
    {
        { FloodDepth.Ankles, "stage1_bottom" },
        { FloodDepth.Waist, "stage2_bottom" },
        { FloodDepth.Chest, "stage3_bottom" },
        { FloodDepth.Submerged, "stage4_bottom" },
    };

    /// <summary>
    ///     Opacity of the liquid tint, so the floor still shows through shallow water.
    /// </summary>
    [DataField]
    public float Alpha = 0.75f;
}
