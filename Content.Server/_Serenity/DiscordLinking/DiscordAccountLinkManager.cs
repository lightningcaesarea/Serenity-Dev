using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Database;
using Content.Server.Discord.DiscordLink;
using Content.Shared._Serenity.CCVar;
using NetCord;
using NetCord.Rest;
using Robust.Server.Player;
using Robust.Server.ServerStatus;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using DiscordInteraction = NetCord.Interaction;

namespace Content.Server._Serenity.DiscordLinking;

/// <summary>
/// Ties each SS14 account to exactly one Discord account and keeps the game behind guild membership.
/// A player without a link is refused at connect and told to press the "Link account" button in Discord; the
/// button hands them a private sign-in link, they sign in with their SS14 account in the browser, and the SS14
/// account site sends them back to <see cref="DiscordOAuth.CallbackPath"/> (see the OAuth partial). Leaving or being
/// banned from the guild locks the account out, and kicks it if it's online.
/// </summary>
public sealed partial class DiscordAccountLinkManager : IPostInjectInit
{
    [Dependency] private DiscordLink _discord = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private ILogManager _log = default!;
    [Dependency] private IStatusHost _statusHost = default!;
    // Discord delivers commands, button presses and member events (and the SS14 account site sends the callback) on
    // their own threads, where the static Loc can't reach the IoC container (it only exists on the main thread) and
    // throws a NullReferenceException. An injected instance is resolved once, up front, and works from any thread.
    [Dependency] private ILocalizationManager _loc = default!;

    public const string PanelButtonId = "serenity-link-open";

    private static readonly TimeSpan DiscordTimeout = TimeSpan.FromSeconds(5);

    private ISawmill _sawmill = default!;

    private bool _required;
    private string _invite = string.Empty;
    private ulong _staffRole;
    private ulong _logChannel;
    private bool _failOpen;

    void IPostInjectInit.PostInject()
    {
        _sawmill = _log.GetSawmill("serenity.discord_link");
    }

    public void Initialize()
    {
        _cfg.OnValueChanged(SerenityCCVars.DiscordLinkRequired, v => _required = v, true);
        _cfg.OnValueChanged(SerenityCCVars.DiscordLinkInvite, v => _invite = v, true);
        _cfg.OnValueChanged(SerenityCCVars.DiscordLinkStaffRole, v => _staffRole = ParseId(v), true);
        _cfg.OnValueChanged(SerenityCCVars.DiscordLinkLogChannel, v => _logChannel = ParseId(v), true);
        _cfg.OnValueChanged(SerenityCCVars.DiscordLinkFailOpen, v => _failOpen = v, true);
        InitializeOAuth();

        _discord.OnInteractionReceived += OnInteraction;
        _discord.OnGuildUserRemoved += OnGuildUserRemoved;
        _discord.RegisterCommandCallback(OnLinkPanelCommand, "linkpanel");
        _discord.RegisterCommandCallback(OnWhoisCommand, "whois");
        _discord.RegisterCommandCallback(OnUnlinkCommand, "unlink");

        if (_required && !_discord.IsConnected)
            _sawmill.Error("serenity.discord_link.required is on but the Discord bot isn't configured; new players will be refused and can't link.");
        else if (_required && !OAuthConfigured)
            _sawmill.Error("serenity.discord_link.required is on but the OAuth settings (oauth_client_id, oauth_client_secret, oauth_redirect_uri) aren't all set; new players will be refused and can't link.");
    }

    public void Shutdown()
    {
        _discord.OnInteractionReceived -= OnInteraction;
        _discord.OnGuildUserRemoved -= OnGuildUserRemoved;
    }

    private static ulong ParseId(string value)
        => ulong.TryParse(value.Trim(), out var id) ? id : 0;

    #region Connection gate

    /// <summary>
    /// Returns a deny message, or null to let the player in. Called for non-admins only.
    /// </summary>
    public async Task<string?> CheckConnection(NetUserId userId, string userName)
    {
        if (!_required)
            return null;

        var link = await _db.GetDiscordLinkByPlayer(userId.UserId);
        if (link == null)
            return UnlinkedMessage();

        var discordId = unchecked((ulong) link.DiscordId);

        GuildUser? member;
        try
        {
            using var cts = new CancellationTokenSource(DiscordTimeout);
            member = await _discord.GetGuildUserAsync(discordId, cts.Token);
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Couldn't check Discord membership for {userName} ({discordId}): {e.Message}");
            return _failOpen ? null : _loc.GetString("serenity-discord-link-deny-unavailable");
        }

        if (member == null)
        {
            bool? banned;
            using (var cts = new CancellationTokenSource(DiscordTimeout))
            {
                try
                {
                    banned = await _discord.IsBannedAsync(discordId, cts.Token);
                }
                catch (Exception)
                {
                    banned = null;
                }
            }

            return banned == true
                ? _loc.GetString("serenity-discord-link-deny-banned")
                : _loc.GetString("serenity-discord-link-deny-not-member", ("invite", InviteText()));
        }

        if (member.Username != link.DiscordUsername)
            await _db.UpdateDiscordUsername(userId.UserId, member.Username);

        return null;
    }

    private string UnlinkedMessage()
    {
        return _loc.GetString("serenity-discord-link-deny-unlinked", ("invite", InviteText()));
    }

    private string InviteText()
        => string.IsNullOrWhiteSpace(_invite) ? _loc.GetString("serenity-discord-link-no-invite") : _invite;

    #endregion

    #region Discord interactions

    private async ValueTask OnInteraction(DiscordInteraction interaction)
    {
        if (interaction is ButtonInteraction button && button.Data.CustomId == PanelButtonId)
            await HandleLinkButton(button);
    }

    private static async Task ReplyPrivately(DiscordInteraction interaction, string content)
    {
        await interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties
        {
            Content = content,
            Flags = MessageFlags.Ephemeral,
            AllowedMentions = AllowedMentionsProperties.None,
        }));
    }

    private async void OnGuildUserRemoved(ulong discordId)
    {
        try
        {
            var link = await _db.GetDiscordLinkByDiscord(discordId);
            if (link == null)
                return;

            var userId = new NetUserId(link.PlayerUserId);
            _tasks.RunOnMainThread(() =>
            {
                if (!_required || !_players.TryGetSessionById(userId, out var session))
                    return;

                session.Channel.Disconnect(_loc.GetString("serenity-discord-link-kick-removed"));
            });

            var located = await _locator.LookupIdAsync(userId);
            await PostLog(LogRemoved(link.DiscordUsername ?? "?", discordId, located?.Username ?? "?", link.PlayerUserId));
        }
        catch (Exception e)
        {
            _sawmill.Error($"Error handling Discord member removal for {discordId}: {e}");
        }
    }

    #endregion

    #region Discord staff commands

    private async void OnLinkPanelCommand(CommandReceivedEventArgs args)
    {
        try
        {
            if (!await IsStaff(args))
                return;

            await _discord.SendMessageAsync(args.Message.ChannelId, new MessageProperties
            {
                Content = _loc.GetString("serenity-discord-link-panel-text"),
                Components =
                [
                    new ActionRowProperties(
                    [
                        new ButtonProperties(PanelButtonId, _loc.GetString("serenity-discord-link-panel-button"), ButtonStyle.Primary),
                    ]),
                ],
            });
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to post link panel: {e}");
        }
    }

    private async void OnWhoisCommand(CommandReceivedEventArgs args)
    {
        try
        {
            if (!await IsStaff(args))
                return;

            if (args.Arguments.Count != 1)
            {
                await Reply(args, _loc.GetString("serenity-discord-link-whois-usage", ("prefix", _discord.BotPrefix)));
                return;
            }

            var link = await ResolveLink(args.Arguments[0]);
            await Reply(args, link == null
                ? _loc.GetString("serenity-discord-link-whois-none")
                : await DescribeLink(link));
        }
        catch (Exception e)
        {
            _sawmill.Error($"whois failed: {e}");
        }
    }

    private async void OnUnlinkCommand(CommandReceivedEventArgs args)
    {
        try
        {
            if (!await IsStaff(args))
                return;

            if (args.Arguments.Count != 1)
            {
                await Reply(args, _loc.GetString("serenity-discord-link-unlink-usage", ("prefix", _discord.BotPrefix)));
                return;
            }

            var link = await ResolveLink(args.Arguments[0]);
            if (link == null || !await _db.RemoveDiscordLink(link.PlayerUserId))
            {
                await Reply(args, _loc.GetString("serenity-discord-link-whois-none"));
                return;
            }

            var description = await DescribeLink(link);
            await Reply(args, _loc.GetString("serenity-discord-link-unlinked", ("link", description)));
            await PostLog(LogUnlinked(description, args.Message.Author.Username, args.Message.Author.Id.ToString()));
        }
        catch (Exception e)
        {
            _sawmill.Error($"unlink failed: {e}");
        }
    }

    private async Task<bool> IsStaff(CommandReceivedEventArgs args)
    {
        if (_staffRole == 0 || args.Message.GuildId == null)
            return false;

        var member = await _discord.GetGuildUserAsync(args.Message.Author.Id);
        return member != null && member.RoleIds.Contains(_staffRole);
    }

    private static async Task Reply(CommandReceivedEventArgs args, string content)
    {
        await args.Message.ReplyAsync(new ReplyMessageProperties
        {
            Content = content,
            AllowedMentions = AllowedMentionsProperties.None,
        });
    }

    /// <summary>
    /// Accepts a Discord mention, a Discord ID, or an SS14 username / user ID.
    /// </summary>
    private async Task<SerenityDiscordLink?> ResolveLink(string query)
    {
        var trimmed = query.Trim().TrimStart('<').TrimEnd('>').TrimStart('@', '!');
        if (ulong.TryParse(trimmed, out var discordId))
            return await _db.GetDiscordLinkByDiscord(discordId);

        var located = await _locator.LookupIdByNameOrIdAsync(query);
        return located == null ? null : await _db.GetDiscordLinkByPlayer(located.UserId.UserId);
    }

    #endregion

    #region Shared helpers

    /// <summary>
    /// One-line summary used by both the bot and the in-game admin commands.
    /// </summary>
    public async Task<string> DescribeLink(SerenityDiscordLink link)
    {
        var located = await _locator.LookupIdAsync(new NetUserId(link.PlayerUserId));
        return _loc.GetString("serenity-discord-link-describe",
            ("player", located?.Username ?? "?"),
            ("userId", link.PlayerUserId.ToString()),
            ("discordId", unchecked((ulong) link.DiscordId).ToString()),
            ("discordName", link.DiscordUsername ?? "?"),
            ("linkedAt", link.LinkedAt.ToString("yyyy-MM-dd HH:mm")));
    }

    // The staff-channel log lines all carry the same identity block, so whoever reads the log can tell who a player is
    // without looking them up: the Discord username and ID and the SS14 name and user ID.

    public string LogLinked(string discordName, ulong discordId, string player, Guid userId)
    {
        return _loc.GetString("serenity-discord-link-log-linked",
            ("discordName", discordName),
            ("discordId", discordId.ToString()),
            ("player", player),
            ("userId", userId.ToString()));
    }

    public string LogRemoved(string discordName, ulong discordId, string player, Guid userId)
    {
        return _loc.GetString("serenity-discord-link-log-removed",
            ("discordName", discordName),
            ("discordId", discordId.ToString()),
            ("player", player),
            ("userId", userId.ToString()));
    }

    /// <param name="link">The one-line link summary from <see cref="DescribeLink"/>, which already names both accounts and IDs.</param>
    /// <param name="by">Who removed it (Discord username or in-game name) and their ID.</param>
    public string LogUnlinked(string link, string by, string byId)
    {
        return _loc.GetString("serenity-discord-link-log-unlinked",
            ("link", link),
            ("by", by),
            ("byId", byId));
    }

    public async Task PostLog(string message)
    {
        if (_logChannel == 0)
            return;

        try
        {
            await _discord.SendMessageAsync(_logChannel, new MessageProperties
            {
                Content = message,
                AllowedMentions = AllowedMentionsProperties.None,
            });
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Couldn't post to the link log channel: {e.Message}");
        }
    }

    #endregion
}
