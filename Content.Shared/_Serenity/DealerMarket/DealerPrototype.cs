using Content.Shared.Roles;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Serenity.DealerMarket;

/// <summary>
/// A named dealer on the station-wide market: what they sell, what they will buy outright, and the kinds of
/// contract they hand out to individual players.
/// </summary>
[Prototype]
public sealed partial class DealerPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The dealer's name as players see it.</summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    /// <summary>One line on what they deal in.</summary>
    [DataField(required: true)]
    public string Trade = string.Empty;

    /// <summary>What they say when a player opens their page.</summary>
    [DataField(required: true)]
    public string Greeting = string.Empty;

    /// <summary>A texture to show as the dealer's portrait.</summary>
    [DataField(required: true)]
    public ResPath Portrait;

    /// <summary>What they sell, delivered by mass driver.</summary>
    [DataField]
    public List<DealerOffer> Stock = new();

    /// <summary>Items this dealer will buy straight off the intake pad; null buys nothing.</summary>
    [DataField]
    public EntityWhitelist? Buys;

    /// <summary>Share of an item's appraised value paid for goods sold outright.</summary>
    [DataField]
    public float BuyRate = 0.6f;

    /// <summary>The contracts this dealer can offer. Each player is dealt their own.</summary>
    [DataField]
    public List<DealerContractTemplate> Contracts = new();
}

/// <summary>One thing a dealer sells.</summary>
[DataDefinition]
public sealed partial class DealerOffer
{
    [DataField(required: true)]
    public EntProtoId Item;

    /// <summary>Price in credits for <see cref="Amount"/> of the item.</summary>
    [DataField(required: true)]
    public int Price;

    /// <summary>How many arrive per purchase.</summary>
    [DataField]
    public int Amount = 1;
}

/// <summary>
/// A kind of job a dealer hands out. Counts are rolled per player and the payout is
/// <see cref="UnitPrice"/> times the rolled count.
/// </summary>
[DataDefinition]
public sealed partial class DealerContractTemplate
{
    [DataField(required: true)]
    public string Title = string.Empty;

    [DataField(required: true)]
    public string Flavour = string.Empty;

    /// <summary>What the dealer wants; every entry must be met to complete the contract.</summary>
    [DataField(required: true)]
    public List<DealerContractWant> Wants = new();

    /// <summary>Flat credits added on top of what the individual wants pay.</summary>
    [DataField]
    public int Bonus;

    /// <summary>
    /// If not empty, only players on one of these jobs are dealt this contract. Contracts with no jobs go to
    /// everyone.
    /// </summary>
    [DataField]
    public List<ProtoId<JobPrototype>> Jobs = new();

    /// <summary>How long the contract stays open after it is dealt, in minutes.</summary>
    [DataField]
    public int LifetimeMinutes = 40;
}

[DataDefinition]
public sealed partial class DealerContractWant
{
    [DataField(required: true)]
    public EntProtoId Item;

    [DataField]
    public int Min = 1;

    [DataField]
    public int Max = 1;

    /// <summary>Credits paid per unit.</summary>
    [DataField(required: true)]
    public int UnitPrice;
}
