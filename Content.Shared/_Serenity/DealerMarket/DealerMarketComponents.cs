using Content.Shared.DeviceLinking;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;

namespace Content.Shared._Serenity.DealerMarket;

/// <summary>
/// The terminal for the dealer market. Its device-link port is wired to a mass driver, which launches purchases
/// back out, and to a <see cref="DealerIntakeComponent"/> pad, where goods are laid out for a dealer to take.
/// </summary>
[RegisterComponent]
public sealed partial class DealerMarketConsoleComponent : Component
{
    [DataField]
    public ProtoId<SourcePortPrototype> LinkingPort = "DealerMarketSender";

    /// <summary>The mass drivers that carry purchases. Each is wired to this console.</summary>
    [ViewVariables]
    public List<EntityUid> Outlets = new();

    /// <summary>The elevators that bring contract crates up.</summary>
    [ViewVariables]
    public List<EntityUid> Elevators = new();

    /// <summary>The pads that goods are collected from.</summary>
    [ViewVariables]
    public List<EntityUid> Intakes = new();
}

/// <summary>
/// A landing pad that mass-driver traffic and hand-carried goods come to rest on. A dealer only takes what is on
/// it when a player asks to sell it outright.
/// </summary>
[RegisterComponent]
public sealed partial class DealerIntakeComponent : Component
{
    /// <summary>How far around the pad goods still count as on it.</summary>
    [DataField]
    public float Range = 0.7f;
}

/// <summary>
/// A lift that brings a contract crate up from below when a player asks for one, and takes it back down with
/// whatever is left in it once the contract is handed over.
/// </summary>
[RegisterComponent]
public sealed partial class DealerElevatorComponent : Component
{
    [DataField]
    public EntProtoId Crate = "DealerContractCrate";

    /// <summary>The crate that is up right now.</summary>
    [ViewVariables]
    public EntityUid? ActiveCrate;
}

/// <summary>Marks a crate as belonging to one player's contract.</summary>
[RegisterComponent]
public sealed partial class DealerContractCrateComponent : Component
{
    [ViewVariables]
    public int ContractId;

    [ViewVariables]
    public NetUserId Buyer;
}

[Serializable, NetSerializable]
public enum DealerElevatorVisuals : byte
{
    Raised,
}
