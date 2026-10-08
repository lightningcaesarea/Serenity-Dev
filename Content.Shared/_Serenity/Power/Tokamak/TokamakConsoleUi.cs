using Robust.Shared.Serialization;

namespace Content.Shared._Serenity.Power.Tokamak;

[Serializable, NetSerializable]
public enum TokamakConsoleUiKey : byte
{
    Key,
}

/// <summary>
/// Appearance keys for the tokamak machines.
/// </summary>
[Serializable, NetSerializable]
public enum TokamakVisuals : byte
{
    CoreState,
    DeviceState,
}

[Serializable, NetSerializable]
public enum TokamakCoreState : byte
{
    Off,
    On,
    Unstable,
}

[Serializable, NetSerializable]
public enum TokamakDeviceState : byte
{
    Off,
    Idle,
    Active,
}

[Serializable, NetSerializable]
public enum TokamakVisualLayers : byte
{
    Base,
    Field,
}

[Serializable, NetSerializable]
public enum TokamakDeviceKind : byte
{
    Gyrotron,
    Injector,
    Harvester,
}

[Serializable, NetSerializable]
public enum TokamakDeviceSetting : byte
{
    Active,
    Rate,
    MegaEnergy,
}

[Serializable, NetSerializable]
public readonly record struct TokamakReactantEntry(string Reactant, float Amount);

[Serializable, NetSerializable]
public readonly record struct TokamakDeviceEntry(
    NetEntity Entity,
    TokamakDeviceKind Kind,
    string Name,
    bool Active,
    bool Aligned,
    float Rate,
    float MaxRate,
    float MegaEnergy,
    float MaxMegaEnergy,
    float RodFill);

[Serializable, NetSerializable]
public sealed class TokamakConsoleBuiState : BoundUserInterfaceState
{
    public bool HasCore;
    public bool Active;
    public bool Powered;
    public int FieldStrength;
    public int MinFieldStrength;
    public int MaxFieldStrength;
    public float PlasmaTemperature;
    public float Instability;
    public float RuptureInstability;
    public float PowerOutput;
    public float PowerDraw;
    public float RadiationLevel;
    public float TotalReactants;
    public float MaxReactants;
    public List<TokamakReactantEntry> Reactants = new();
    public List<TokamakDeviceEntry> Devices = new();
}

[Serializable, NetSerializable]
public sealed class TokamakSetActiveMessage(bool active) : BoundUserInterfaceMessage
{
    public bool Active { get; } = active;
}

[Serializable, NetSerializable]
public sealed class TokamakSetFieldStrengthMessage(int fieldStrength) : BoundUserInterfaceMessage
{
    public int FieldStrength { get; } = fieldStrength;
}

[Serializable, NetSerializable]
public sealed class TokamakScramMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class TokamakDeviceSettingMessage(NetEntity device, TokamakDeviceSetting setting, float value)
    : BoundUserInterfaceMessage
{
    public NetEntity Device { get; } = device;
    public TokamakDeviceSetting Setting { get; } = setting;
    public float Value { get; } = value;
}
