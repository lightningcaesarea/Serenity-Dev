using Robust.Shared.Serialization;

namespace Content.Shared._Serenity.Economy;

/// <summary>
/// Client asks for the balance of each of its characters, to show in the lobby.
/// </summary>
[Serializable, NetSerializable]
public sealed class RequestCharacterBalancesEvent : EntityEventArgs;

/// <summary>
/// Server's answer to <see cref="RequestCharacterBalancesEvent"/>, also re-sent whenever the balance of the
/// character being played changes.
/// </summary>
[Serializable, NetSerializable]
public sealed class CharacterBalancesEvent(Dictionary<int, double> balances, Dictionary<int, double> startingFunds, double startingBalance)
    : EntityEventArgs
{
    /// <summary>Balance by character slot. A character that has never spawned is absent.</summary>
    public readonly Dictionary<int, double> Balances = balances;

    /// <summary>How much of each balance is still starting funds (spendable, not transferable), by slot.</summary>
    public readonly Dictionary<int, double> StartingFunds = startingFunds;

    /// <summary>What a character absent from <see cref="Balances"/> will start with.</summary>
    public readonly double StartingBalance = startingBalance;
}
