namespace Content.Shared._Serenity.Chemistry;

/// <summary>
/// The body sprite state of a storage tank. A tank spray painted to look like another tank copies the other
/// tank's body state from this, and its fill settings from its <see cref="Content.Shared.Chemistry.Components.SolutionContainerVisualsComponent"/>,
/// so the fill always matches the body it sits in.
/// </summary>
[RegisterComponent]
public sealed partial class PaintableTankVisualsComponent : Component
{
    /// <summary>State drawn on the sprite's first layer.</summary>
    [DataField(required: true)]
    public string BaseState = string.Empty;
}
