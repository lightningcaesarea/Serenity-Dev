using Robust.Shared.Configuration;

namespace Content.Shared._Serenity.CCVar;

public sealed partial class SerenityCCVars
{
    /// <summary>
    /// Whether shipyard consoles offer the "Request dock" verb that spawns an extra dock in space.
    /// </summary>
    public static readonly CVarDef<bool> DocksEnabled =
        CVarDef.Create("serenity.docks.enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// How far from the requesting console's grid, in tiles, a new dock is placed.
    /// </summary>
    public static readonly CVarDef<float> DocksDistance =
        CVarDef.Create("serenity.docks.distance", 160f, CVar.SERVERONLY);

    /// <summary>
    /// The most docks that can exist at once.
    /// </summary>
    public static readonly CVarDef<int> DocksMax =
        CVarDef.Create("serenity.docks.max", 3, CVar.SERVERONLY);

    /// <summary>
    /// Seconds that must pass after a dock is spawned before another can be requested.
    /// </summary>
    public static readonly CVarDef<float> DocksCooldown =
        CVarDef.Create("serenity.docks.cooldown", 300f, CVar.SERVERONLY);
}
