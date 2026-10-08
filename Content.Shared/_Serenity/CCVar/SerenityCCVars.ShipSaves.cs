using Robust.Shared.Configuration;

namespace Content.Shared._Serenity.CCVar;

public sealed partial class SerenityCCVars
{
    /// <summary>
    /// Whether players can save their ship at a shipyard console and load it again in a later round.
    /// </summary>
    public static readonly CVarDef<bool> ShipSavesEnabled =
        CVarDef.Create("serenity.ship_saves.enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// How many saved ships each character can keep.
    /// </summary>
    public static readonly CVarDef<int> ShipSavesPerCharacter =
        CVarDef.Create("serenity.ship_saves.per_character", 3, CVar.SERVERONLY);

    /// <summary>
    /// Fraction of a saved ship's value charged to load it again.
    /// </summary>
    public static readonly CVarDef<float> ShipSaveLoadFee =
        CVarDef.Create("serenity.ship_saves.load_fee", 0.1f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds a player has to wait between loading saved ships.
    /// </summary>
    public static readonly CVarDef<float> ShipSaveLoadCooldown =
        CVarDef.Create("serenity.ship_saves.load_cooldown", 300f, CVar.SERVERONLY);
}
