// Ported from Frontier Station 14 at commit 5be37d18c2 (2024-07-01), MIT licensed.
// Copyright (c) 2017-2024 New Frontiers. Adapted for Serenity.

using Robust.Shared.GameStates;
using Robust.Shared.Network;

namespace Content.Shared._Serenity.Shipyard.Components;

/// <summary>
/// Proof of ownership of a ship. Lives on the owner's ID card and on the ship grid itself.
/// One ship per card.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(SharedShipyardSystem))]
public sealed partial class ShuttleDeedComponent : Component
{
    public const int MaxNameLength = 30;
    public const int MaxSuffixLength = 3 + 1 + 4; // 3 digits, dash, up to 4 letters

    [DataField, AutoNetworkedField]
    public EntityUid? ShuttleUid;

    [DataField, AutoNetworkedField]
    public string? ShuttleName = "Unknown";

    [DataField, AutoNetworkedField]
    public string? ShuttleNameSuffix;

    /// <summary>
    /// The mob that bought the ship.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ShuttleOwner;

    /// <summary>
    /// Account that bought the ship. Server-only; recorded so a future persistence layer can
    /// tie the ship to a player rather than a round-scoped mob.
    /// </summary>
    [DataField]
    public NetUserId? OwnerUserId;

    /// <summary>
    /// How much of the price was paid in bills bound to a character (their starting funds), and whose they were.
    /// Selling the ship pays that much back as bills bound to the same character, so a ship can't turn starting
    /// funds into ordinary money. Server-only.
    /// </summary>
    [DataField]
    public int BoundPaid;

    [DataField]
    public int? BoundProfileId;

    [DataField]
    public string BoundOwnerName = string.Empty;

    /// <summary>
    /// Serenity: the ship was brought back from a save rather than bought, so it can't be sold. Server-only.
    /// </summary>
    [DataField]
    public bool LoadedFromSave;
}
