// Ported from Frontier Station 14 at commit 5be37d18c2 (2024-07-01), MIT licensed.
// Copyright (c) 2017-2024 New Frontiers. Adapted for Serenity.

using Robust.Shared.Serialization;

namespace Content.Shared._Serenity.Shipyard.Events;

/// <summary>
/// Buy the vessel with this prototype ID.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipyardConsolePurchaseMessage : BoundUserInterfaceMessage
{
    public string Vessel { get; }

    public ShipyardConsolePurchaseMessage(string vessel)
    {
        Vessel = vessel;
    }
}

/// <summary>
/// Sell the ship deeded to the inserted ID card. Everything is validated server-side.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipyardConsoleSellMessage : BoundUserInterfaceMessage;

/// <summary>
/// Serenity: save the ship deeded to the inserted ID card for a later round. The ship leaves the sector.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipyardConsoleSaveMessage : BoundUserInterfaceMessage;

/// <summary>
/// Serenity: bring back one of the playing character's saved ships. Only the save's id comes from the client;
/// the ship itself is read from the server's copy.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipyardConsoleLoadMessage : BoundUserInterfaceMessage
{
    public string SaveId { get; }

    public ShipyardConsoleLoadMessage(string saveId)
    {
        SaveId = saveId;
    }
}
