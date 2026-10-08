// Ported from Frontier Station 14 at commit 5be37d18c2 (2024-07-01), MIT licensed.
// Copyright (c) 2017-2024 New Frontiers. Adapted for Serenity.

using Robust.Shared.Serialization;

namespace Content.Shared._Serenity.Shipyard.BUI;

[NetSerializable, Serializable]
public sealed class ShipyardConsoleInterfaceState : BoundUserInterfaceState
{
    public readonly int Balance;
    public readonly bool AccessGranted;
    public readonly string? ShipDeedTitle;
    public readonly int ShipSellValue;
    public readonly bool IsTargetIdPresent;
    public readonly List<string> ShipyardPrototypes;
    public readonly List<SavedShipEntry> SavedShips; // Serenity
    public readonly bool CanSaveShips; // Serenity

    public ShipyardConsoleInterfaceState(
        int balance,
        bool accessGranted,
        string? shipDeedTitle,
        int shipSellValue,
        bool isTargetIdPresent,
        List<string> shipyardPrototypes,
        List<SavedShipEntry> savedShips,
        bool canSaveShips)
    {
        Balance = balance;
        AccessGranted = accessGranted;
        ShipDeedTitle = shipDeedTitle;
        ShipSellValue = shipSellValue;
        IsTargetIdPresent = isTargetIdPresent;
        ShipyardPrototypes = shipyardPrototypes;
        SavedShips = savedShips;
        CanSaveShips = canSaveShips;
    }
}

/// <summary>
/// Serenity: one of the playing character's saved ships, as the console lists it.
/// </summary>
[NetSerializable, Serializable]
public sealed record SavedShipEntry(string Id, string Name, int LoadFee);
