using System.Numerics;

namespace Content.Shared._Serenity.Power.Tokamak;

public static class TokamakBeam
{
    /// <summary>
    /// How far off the beam axis a core can be and still be hit, in tiles.
    /// </summary>
    public const float BeamWidth = 0.6f;

    /// <summary>
    /// Whether a device at <paramref name="devicePos"/> facing <paramref name="deviceRotation"/> points at a core at
    /// <paramref name="corePos"/> within <paramref name="range"/> tiles.
    /// </summary>
    public static bool IsAligned(Vector2 devicePos, Angle deviceRotation, Vector2 corePos, float range)
    {
        var facing = deviceRotation.GetDir().ToVec();
        var delta = corePos - devicePos;
        var along = Vector2.Dot(delta, facing);
        if (along < 0.25f || along > range)
            return false;

        var lateral = MathF.Abs(delta.X * facing.Y - delta.Y * facing.X);
        return lateral <= BeamWidth;
    }
}
