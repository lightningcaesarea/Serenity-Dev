using Robust.Shared.GameStates;

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
}
