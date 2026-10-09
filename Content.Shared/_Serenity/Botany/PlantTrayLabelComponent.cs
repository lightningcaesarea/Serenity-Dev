namespace Content.Shared._Serenity.Botany;

/// <summary>
/// Remembers a hydroponics tray's own name, so it can show what is growing in it and go back afterwards.
/// </summary>
[RegisterComponent]
public sealed partial class PlantTrayLabelComponent : Component
{
    [DataField]
    public string? BaseName;
}
