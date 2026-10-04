using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Serenity.Chemistry;

/// <summary>
/// Lets a container switch between being poured into and being drawn from, with an alt-click verb.
/// The mode is applied by swapping <see cref="Content.Shared.Chemistry.Components.RefillableSolutionComponent"/>
/// and <see cref="Content.Shared.Chemistry.Components.DrainableSolutionComponent"/>, because the solution transfer
/// system always prefers pouring into a target that has both.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(ToggleableSolutionTransferSystem))]
public sealed partial class ToggleableSolutionTransferComponent : Component
{
    /// <summary>Solution that containers pour into or draw from.</summary>
    [DataField(required: true), AutoNetworkedField]
    public string Solution = string.Empty;

    /// <summary>True: containers used on this pour into it. False: they draw from it.</summary>
    [DataField, AutoNetworkedField]
    public bool Filling = true;

    /// <summary>
    /// White sprite state, in the entity's own RSI, drawn unshaded on top and tinted by mode.
    /// Null for no mode light.
    /// </summary>
    [DataField]
    public string? LightState;

    [DataField]
    public Color FillingLightColor = Color.FromHex("#4fe36a");

    [DataField]
    public Color DispensingLightColor = Color.FromHex("#ffa62b");
}

[Serializable, NetSerializable]
public enum ToggleableSolutionTransferVisuals : byte
{
    Filling,
}

public enum ToggleableSolutionTransferLayers : byte
{
    Light,
}
