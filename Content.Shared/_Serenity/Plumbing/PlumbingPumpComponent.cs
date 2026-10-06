using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._Serenity.Plumbing;

/// <summary>
///     On/off switch and rate control for a plumbing machine that pulls through a
///     <see cref="Content.Shared._Starlight.Plumbing.Components.PlumbingInletComponent"/>, like the one-way valve.
///     Drives the inlet's transfer amount: the set rate (times the plumbing update interval) while on, nothing while off.
/// </summary>
[RegisterComponent]
public sealed partial class PlumbingPumpComponent : Component
{
    [DataField]
    public bool Enabled = true;

    /// <summary>
    ///     Units per second while on. Whole units only.
    /// </summary>
    [DataField]
    public FixedPoint2 TransferRate = FixedPoint2.New(10);

    [DataField]
    public FixedPoint2 MaxTransferRate = FixedPoint2.New(20);

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
public sealed class PlumbingPumpBoundUserInterfaceState(bool enabled, float transferRate, float maxTransferRate)
    : BoundUserInterfaceState
{
    public readonly bool Enabled = enabled;
    public readonly float TransferRate = transferRate;
    public readonly float MaxTransferRate = maxTransferRate;
}

[Serializable, NetSerializable]
public sealed class PlumbingPumpToggleMessage(bool enabled) : BoundUserInterfaceMessage
{
    public readonly bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class PlumbingPumpSetRateMessage(float transferRate) : BoundUserInterfaceMessage
{
    public readonly float TransferRate = transferRate;
}
