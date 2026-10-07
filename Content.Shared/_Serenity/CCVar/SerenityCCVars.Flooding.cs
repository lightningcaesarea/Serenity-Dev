using Robust.Shared.Configuration;

namespace Content.Shared._Serenity.CCVar;

public sealed partial class SerenityCCVars
{
    /// <summary>
    /// Whether floods flow at all. Turning it off freezes every flood where it stands.
    /// </summary>
    public static readonly CVarDef<bool> FloodEnabled =
        CVarDef.Create("serenity.flood.enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Seconds between two flow steps of the same flooded tile. Lower is faster water.
    /// </summary>
    public static readonly CVarDef<float> FloodFlowDelay =
        CVarDef.Create("serenity.flood.flow_delay", 0.2f, CVar.SERVERONLY);

    /// <summary>
    /// Milliseconds of server time flooding may use per tick. Tiles that do not fit wait for the next tick.
    /// </summary>
    public static readonly CVarDef<float> FloodTickBudget =
        CVarDef.Create("serenity.flood.tick_budget_ms", 2f, CVar.SERVERONLY);

    /// <summary>
    /// A puddle holding at least this many units turns into a flood.
    /// </summary>
    public static readonly CVarDef<float> FloodFromPuddle =
        CVarDef.Create("serenity.flood.from_puddle_units", 300f, CVar.SERVERONLY);

    /// <summary>
    /// A flood holding less than this turns back into a puddle, and a flood never spreads onto a new tile
    /// unless both tiles would end up holding at least this much.
    /// </summary>
    public static readonly CVarDef<float> FloodMinimum =
        CVarDef.Create("serenity.flood.minimum_units", 60f, CVar.SERVERONLY);

    /// <summary>
    /// Neighbouring floods closer than this in volume count as level and stop flowing.
    /// </summary>
    public static readonly CVarDef<float> FloodLevelTolerance =
        CVarDef.Create("serenity.flood.level_tolerance_units", 4f, CVar.SERVERONLY);

    /// <summary>
    /// Units at which a flood is waist deep.
    /// </summary>
    public static readonly CVarDef<float> FloodWaistDepth =
        CVarDef.Create("serenity.flood.waist_units", 200f, CVar.SERVERONLY);

    /// <summary>
    /// Units at which a flood is chest deep. Anyone lying down in it is under water.
    /// </summary>
    public static readonly CVarDef<float> FloodChestDepth =
        CVarDef.Create("serenity.flood.chest_units", 300f, CVar.SERVERONLY);

    /// <summary>
    /// Units at which a flood is over everyone's head and nobody standing in it can breathe.
    /// </summary>
    public static readonly CVarDef<float> FloodSubmergedDepth =
        CVarDef.Create("serenity.flood.submerged_units", 400f, CVar.SERVERONLY);

    /// <summary>
    /// Units moved in one flow step before it splashes.
    /// </summary>
    public static readonly CVarDef<float> FloodSplashUnits =
        CVarDef.Create("serenity.flood.splash_units", 60f, CVar.SERVERONLY);

    /// <summary>
    /// How much faster a floor drain swallows flood water than it swallows puddles.
    /// </summary>
    public static readonly CVarDef<float> FloodDrainMultiplier =
        CVarDef.Create("serenity.flood.drain_multiplier", 10f, CVar.SERVERONLY);
}
