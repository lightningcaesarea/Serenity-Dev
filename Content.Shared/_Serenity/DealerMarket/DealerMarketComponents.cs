using Content.Shared.DeviceLinking;
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

    /// <summary>The pads that goods are collected from.</summary>
    [ViewVariables]
    public List<EntityUid> Intakes = new();
}

/// <summary>
/// A landing pad that mass-driver traffic and hand-carried goods come to rest on. Nothing is taken from it until
/// a player asks a dealer to.
/// </summary>
[RegisterComponent]
public sealed partial class DealerIntakeComponent : Component
{
    /// <summary>How far around the pad goods still count as on it.</summary>
    [DataField]
    public float Range = 0.7f;
}
