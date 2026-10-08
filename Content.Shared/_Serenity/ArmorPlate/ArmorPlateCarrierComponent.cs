using Robust.Shared.GameStates;

namespace Content.Shared._Serenity.ArmorPlate;

/// <summary>
/// Clothing that can carry an armor plate in its storage. The first plate in the storage is the active one.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArmorPlateCarrierComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? ActivePlate;

    /// <summary>
    /// Whether the wearer gets a popup when the active plate breaks.
    /// </summary>
    [DataField]
    public bool BreakPopup = true;
}
