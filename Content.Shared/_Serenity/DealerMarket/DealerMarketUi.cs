using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Serenity.DealerMarket;

[NetSerializable, Serializable]
public enum DealerMarketUiKey : byte
{
    Key,
}

/// <summary>One line of what a contract asks for.</summary>
[NetSerializable, Serializable]
public sealed record DealerContractWantInfo(string Name, int Wanted, int OnPad);

/// <summary>A contract dealt to the player looking at the terminal.</summary>
[NetSerializable, Serializable]
public sealed record DealerContractInfo(
    int Id,
    ProtoId<DealerPrototype> Dealer,
    string Title,
    string Flavour,
    List<DealerContractWantInfo> Wants,
    int Payout,
    int SecondsLeft,
    bool Ready,
    bool RoleContract);

/// <summary>A good sitting on the pad, as the terminal lists it.</summary>
[NetSerializable, Serializable]
public sealed record DealerPadItem(string Name, int Count);

/// <summary>Sent to one player at a time, because each has their own contracts.</summary>
[NetSerializable, Serializable]
public sealed class DealerMarketStateMessage : BoundUserInterfaceMessage
{
    public readonly int Balance;
    public readonly bool HasIntake;
    public readonly bool HasOutlet;
    public readonly int Incoming;
    public readonly List<DealerContractInfo> Contracts;
    public readonly List<DealerPadItem> Pad;
    /// <summary>Credits each dealer would pay for the whole pad right now.</summary>
    public readonly Dictionary<ProtoId<DealerPrototype>, int> Quotes;

    public DealerMarketStateMessage(
        int balance,
        bool hasIntake,
        bool hasOutlet,
        int incoming,
        List<DealerContractInfo> contracts,
        List<DealerPadItem> pad,
        Dictionary<ProtoId<DealerPrototype>, int> quotes)
    {
        Balance = balance;
        HasIntake = hasIntake;
        HasOutlet = hasOutlet;
        Incoming = incoming;
        Contracts = contracts;
        Pad = pad;
        Quotes = quotes;
    }
}

/// <summary>Hand over what is on the pad against a contract.</summary>
[NetSerializable, Serializable]
public sealed class DealerFulfilMessage(int id) : BoundUserInterfaceMessage
{
    public readonly int Id = id;
}

/// <summary>Turn a contract down; a replacement is dealt after a wait.</summary>
[NetSerializable, Serializable]
public sealed class DealerDeclineMessage(int id) : BoundUserInterfaceMessage
{
    public readonly int Id = id;
}

[NetSerializable, Serializable]
public sealed class DealerBuyMessage(ProtoId<DealerPrototype> dealer, int offer) : BoundUserInterfaceMessage
{
    public readonly ProtoId<DealerPrototype> Dealer = dealer;
    public readonly int Offer = offer;
}

/// <summary>Sell everything the dealer will take off the pad at their standing rate.</summary>
[NetSerializable, Serializable]
public sealed class DealerSellMessage(ProtoId<DealerPrototype> dealer) : BoundUserInterfaceMessage
{
    public readonly ProtoId<DealerPrototype> Dealer = dealer;
}
