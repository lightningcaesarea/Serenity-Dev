using Robust.Shared.Configuration;

namespace Content.Shared._Serenity.CCVar;

/// <summary>
/// CVars owned by Serenity. Keep them under the <c>serenity.</c> prefix.
/// </summary>
[CVarDefs]
public sealed partial class SerenityCCVars
{
    /// <summary>
    /// Whether an automated bus runs between every grid carrying a StationTransit component.
    /// Needs at least two stops to be useful.
    /// </summary>
    public static readonly CVarDef<bool> PublicTransit =
        CVarDef.Create("serenity.publictransit.enabled", false, CVar.SERVERONLY);

    /// <summary>
    /// Grid file loaded as the bus.
    /// </summary>
    public static readonly CVarDef<string> PublicTransitBusMap =
        CVarDef.Create("serenity.publictransit.bus_map", "/Maps/_Serenity/Shuttles/publicts.yml", CVar.SERVERONLY);

    /// <summary>
    /// Seconds the bus waits at each stop.
    /// </summary>
    public static readonly CVarDef<float> PublicTransitWaitTime =
        CVarDef.Create("serenity.publictransit.wait_time", 150f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds the bus spends in FTL between stops.
    /// </summary>
    public static readonly CVarDef<float> PublicTransitFlyTime =
        CVarDef.Create("serenity.publictransit.fly_time", 145f, CVar.SERVERONLY);

    /// <summary>
    /// Whether points of interest are loaded onto the default map at round start.
    /// </summary>
    public static readonly CVarDef<bool> PoiSpawnerEnabled =
        CVarDef.Create("serenity.poi.enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Refuse connections from accounts that are not linked to a Discord account that is still in the guild.
    /// Needs the Discord bot configured (discord.token, discord.guild_id, discord.prefix). Admins are exempt.
    /// </summary>
    public static readonly CVarDef<bool> DiscordLinkRequired =
        CVarDef.Create("serenity.discord_link.required", false, CVar.SERVERONLY);

    /// <summary>
    /// Invite shown to players who need to join or link, e.g. https://discord.gg/abc.
    /// </summary>
    public static readonly CVarDef<string> DiscordLinkInvite =
        CVarDef.Create("serenity.discord_link.invite", "", CVar.SERVERONLY);

    /// <summary>
    /// Minutes the sign-in link the Discord button hands out stays valid.
    /// </summary>
    public static readonly CVarDef<int> DiscordLinkSignInMinutes =
        CVarDef.Create("serenity.discord_link.signin_minutes", 10, CVar.SERVERONLY);

    /// <summary>
    /// Client ID of the OAuth application registered on the SS14 account site (Manage account > Developer).
    /// </summary>
    public static readonly CVarDef<string> DiscordLinkOAuthClientId =
        CVarDef.Create("serenity.discord_link.oauth_client_id", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    /// Client secret of that OAuth application. Set it in the config file only, never through a command.
    /// </summary>
    public static readonly CVarDef<string> DiscordLinkOAuthClientSecret =
        CVarDef.Create("serenity.discord_link.oauth_client_secret", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    /// The public HTTPS address the SS14 account site sends players back to after they sign in. It has to match the
    /// redirect URI registered with the OAuth application exactly, and end in /discord-link/callback.
    /// </summary>
    public static readonly CVarDef<string> DiscordLinkOAuthRedirectUri =
        CVarDef.Create("serenity.discord_link.oauth_redirect_uri", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    /// Discord role ID allowed to run the bot's staff commands (link panel, whois). Empty disables them.
    /// </summary>
    public static readonly CVarDef<string> DiscordLinkStaffRole =
        CVarDef.Create("serenity.discord_link.staff_role", "", CVar.SERVERONLY);

    /// <summary>
    /// Discord channel ID that receives link, unlink and removal notices. Empty disables them.
    /// </summary>
    public static readonly CVarDef<string> DiscordLinkLogChannel =
        CVarDef.Create("serenity.discord_link.log_channel", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    /// When Discord can't be reached, let already-linked players in (true) or refuse everyone (false).
    /// Unlinked players are always refused while linking is required.
    /// </summary>
    public static readonly CVarDef<bool> DiscordLinkFailOpen =
        CVarDef.Create("serenity.discord_link.fail_open", true, CVar.SERVERONLY);

    /// <summary>
    /// Discord channel ID of the ahelp relay. Staff (serenity.discord_link.staff_role) replying to an ahelp there
    /// sends the reply to the player in game. If discord.ahelp_webhook is empty the bot posts the ahelps here itself.
    /// Empty falls back to the channel the ahelp webhook posts in.
    /// </summary>
    public static readonly CVarDef<string> DiscordAhelpChannel =
        CVarDef.Create("serenity.discord.ahelp_channel", "", CVar.SERVERONLY);

    /// <summary>
    /// Show the connected player count and round time as the Discord bot's status.
    /// </summary>
    public static readonly CVarDef<bool> DiscordStatusEnabled =
        CVarDef.Create("serenity.discord.status_enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Seconds between Discord bot status updates. Discord rate-limits presence changes, so keep this at 20 or more.
    /// </summary>
    public static readonly CVarDef<float> DiscordStatusInterval =
        CVarDef.Create("serenity.discord.status_interval", 30f, CVar.SERVERONLY);

    /// <summary>
    /// Federal Bills a character's account opens with, the first time that character spawns.
    /// Money is per-character, so every new character can claim this once.
    /// </summary>
    public static readonly CVarDef<int> CharacterStartingBalance =
        CVarDef.Create("serenity.economy.starting_balance", 20000, CVar.SERVERONLY);

    /// <summary>
    /// Whether ghosts may return themselves to the lobby with <c>ghostrespawn</c> once the timer is up.
    /// </summary>
    public static readonly CVarDef<bool> RespawnEnabled =
        CVarDef.Create("serenity.respawn.enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Seconds a player must have been a ghost before they can respawn as a new character.
    /// </summary>
    public static readonly CVarDef<float> RespawnTime =
        CVarDef.Create("serenity.respawn.time", 60f, CVar.SERVERONLY);

    /// <summary>
    /// Whether player-owned ships (anything carrying a ShuttleDeed) FTL to CentComm when the round ends.
    /// </summary>
    public static readonly CVarDef<bool> RoundEndShipFtl =
        CVarDef.Create("serenity.roundend_ship_ftl.enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Seconds of FTL startup before an owned ship leaves at round end.
    /// </summary>
    public static readonly CVarDef<float> RoundEndShipFtlStartup =
        CVarDef.Create("serenity.roundend_ship_ftl.startup", 15f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds an owned ship spends in hyperspace on its way to CentComm at round end.
    /// </summary>
    public static readonly CVarDef<float> RoundEndShipFtlTravel =
        CVarDef.Create("serenity.roundend_ship_ftl.travel", 30f, CVar.SERVERONLY);

    /// <summary>
    /// Whether the sector event scheduler (bluespace-error grids: vaults, caches, lost vessels) runs each round.
    /// </summary>
    public static readonly CVarDef<bool> SectorEventsEnabled =
        CVarDef.Create("serenity.sector_events.enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Minutes a body stays in the cryo stasis dimension before it (and everything it carries) is deleted.
    /// </summary>
    public static readonly CVarDef<float> CryoStasisLifetime =
        CVarDef.Create("serenity.cryo.stasis_lifetime_minutes", 120f, CVar.SERVERONLY);
}
