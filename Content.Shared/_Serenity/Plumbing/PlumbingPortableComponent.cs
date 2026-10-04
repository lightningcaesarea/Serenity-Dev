using Content.Shared.FixedPoint;
using Robust.Shared.Audio;

namespace Content.Shared._Serenity.Plumbing;

/// <summary>
///     A movable reagent tank that can dock with a fluid connector port.
///     The tank can be anchored anywhere; it only joins a plumbing network when anchored on top of a port.
///     The valve decides whether it feeds the network, drains the network into itself, or does nothing.
/// </summary>
[RegisterComponent]
public sealed partial class PlumbingPortableComponent : Component
{
    /// <summary>
    ///     The plumbing node that links to a port on the same tile.
    /// </summary>
    [DataField]
    public string NodeName = "portable";

    /// <summary>
    ///     The tank's solution, used both for supplying and for filling.
    /// </summary>
    [DataField]
    public string SolutionName = "tank";

    [DataField]
    public PlumbingPortableMode Mode = PlumbingPortableMode.Closed;

    /// <summary>
    ///     How much the tank pulls from the network per plumbing update while filling.
    /// </summary>
    [DataField]
    public FixedPoint2 TransferAmount = FixedPoint2.New(20);

    [DataField]
    public SoundSpecifier ValveSound = new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg");

    public int RoundRobinIndex;
}

public enum PlumbingPortableMode : byte
{
    /// <summary>
    ///     Docked but isolated: nothing flows in or out.
    /// </summary>
    Closed,

    /// <summary>
    ///     Other machines on the network can pull from the tank.
    /// </summary>
    Supply,

    /// <summary>
    ///     The tank pulls from the network's outlets into itself.
    /// </summary>
    Fill,
}
