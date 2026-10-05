using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._Serenity.Plumbing;

/// <summary>
///     On/off switch and rate control for a plumbing machine that pulls through a
///     <see cref="Content.Shared._Starlight.Plumbing.Components.PlumbingInletComponent"/>, like the one-way valve.
///     Drives the inlet's transfer amount: the set rate while on, nothing while off.
/// </summary>
[RegisterComponent]
public sealed partial class PlumbingPumpComponent : Component
{
    [DataField]
    public bool Enabled = true;

    /// <summary>
    ///     Units pulled per plumbing update while on.
    /// </summary>
    [DataField]
    public FixedPoint2 TransferAmount = FixedPoint2.New(20);

    [DataField]
    public FixedPoint2 MaxTransferAmount = FixedPoint2.New(40);

    [DataField]
    public SoundSpecifier ClickSound = new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg");
}

[Serializable, NetSerializable]
public enum PlumbingPumpUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum PlumbingPumpVisuals : byte
{
    Enabled,
}

[Serializable, NetSerializable]
public sealed class PlumbingPumpBoundUserInterfaceState(bool enabled, float transferAmount, float maxTransferAmount)
    : BoundUserInterfaceState
{
    public readonly bool Enabled = enabled;
    public readonly float TransferAmount = transferAmount;
    public readonly float MaxTransferAmount = maxTransferAmount;
}

[Serializable, NetSerializable]
public sealed class PlumbingPumpToggleMessage(bool enabled) : BoundUserInterfaceMessage
{
    public readonly bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class PlumbingPumpSetRateMessage(float transferAmount) : BoundUserInterfaceMessage
{
    public readonly float TransferAmount = transferAmount;
}
