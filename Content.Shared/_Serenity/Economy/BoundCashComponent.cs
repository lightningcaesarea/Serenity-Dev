using Robust.Shared.GameStates;

namespace Content.Shared._Serenity.Economy;

/// <summary>
/// Federal Bills withdrawn from a character's starting funds. Only that character can hold, merge, spend or deposit
/// them, so a throwaway character's starting money can't be handed to anyone else.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BoundCashComponent : Component
{
    /// <summary>The DB id of the character the bills belong to.</summary>
    [DataField, AutoNetworkedField]
    public int ProfileId;

    /// <summary>The body they were withdrawn by, for client prediction; the server also accepts the character in a new body.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? OwnerEntity;

    [DataField, AutoNetworkedField]
    public string OwnerName = string.Empty;
}
