using Robust.Shared.Serialization;

namespace Content.Shared._Serenity.Flooding;

/// <summary>
///     How far up a person standing in a flood the liquid comes.
/// </summary>
[Serializable, NetSerializable]
public enum FloodDepth : byte
{
    Ankles,
    Waist,
    Chest,
    Submerged,
}

[Serializable, NetSerializable]
public enum FloodVisuals : byte
{
    Depth,
    Color,
}

[Serializable, NetSerializable]
public enum FloodVisualLayers : byte
{
    Liquid,
}
