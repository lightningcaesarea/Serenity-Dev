using System.Linq;
using System.Threading.Tasks;
using Content.Server.Discord;
using Content.Server.Discord.DiscordLink;
using Content.Shared._Serenity.CCVar;
using Content.Shared.Administration;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using Robust.Shared.Asynchronous;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Server.Administration.Systems;

/// <summary>
/// Serenity: ties the ahelp relay channel to the Discord bot. Staff replying (Discord's "Reply") to an ahelp
/// in that channel sends the reply to the player in game, as an admin message. Without an ahelp webhook,
/// the bot posts and updates the ahelp embeds in the channel itself.
/// </summary>
public sealed partial class BwoinkSystem
{
    [Dependency] private DiscordLink _discordLink = default!;
    [Dependency] private ITaskManager _taskManager = default!;

    private const string DeliveredReaction = "✅";
    private const string OfflineReaction = "💤";
    private const string UnknownAhelpReaction = "❓";
    private const string NotStaffReaction = "⛔";

    private ulong _botAhelpChannel;
    private ulong _ahelpStaffRole;

    /// <summary>
    /// Discord message ID of every ahelp embed relayed since the server started, to the player it belongs to.
    /// </summary>
    private readonly Dictionary<ulong, NetUserId> _ahelpEmbedOwners = new();

    /// <summary>
    /// The bot posts the ahelp embeds itself: there's no webhook, but there is a channel and a bot.
    /// </summary>
    private bool BotPostsAhelps => _webhookUrl == string.Empty && _botAhelpChannel != 0 && _discordLink.IsConnected;

    /// <summary>
    /// Ahelps are relayed to Discord at all, by the webhook or by the bot.
    /// </summary>
    private bool RelaysAhelps => _webhookUrl != string.Empty || BotPostsAhelps;

    private void InitializeDiscordBot()
    {
        Subs.CVar(_config, SerenityCCVars.DiscordAhelpChannel, OnBotAhelpChannelChanged, true);
        Subs.CVar(_config, SerenityCCVars.DiscordLinkStaffRole, v => _ahelpStaffRole = ParseDiscordId(v), true);
        _discordLink.OnMessageReceived += OnDiscordMessage;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _discordLink.OnMessageReceived -= OnDiscordMessage;
    }

    private void OnBotAhelpChannelChanged(string value)
    {
        _botAhelpChannel = ParseDiscordId(value);
        RaiseNetworkEvent(new BwoinkDiscordRelayUpdated(RelaysAhelps));
    }

    private static ulong ParseDiscordId(string value)
    {
        return ulong.TryParse(value.Trim(), out var id) ? id : 0;
    }

    /// <summary>
    /// The channel staff reply in: the configured one, else the one the ahelp webhook posts in.
    /// </summary>
    private ulong AhelpReplyChannel()
    {
        if (_botAhelpChannel != 0)
            return _botAhelpChannel;

        return ParseDiscordId(_webhookData?.ChannelId ?? string.Empty);
    }

    /// <summary>
    /// Guild and channel for "go to ahelp" links to the relayed embeds.
    /// </summary>
    private bool TryGetRelayLocation(out string guildId, out string channelId)
    {
        if (BotPostsAhelps && _config.GetCVar(Content.Shared.CCVar.CCVars.DiscordGuildId) is { Length: > 0 } guild)
        {
            guildId = guild;
            channelId = _botAhelpChannel.ToString();
            return true;
        }

        if (_webhookData is { GuildId: { } webhookGuild, ChannelId: { } webhookChannel })
        {
            guildId = webhookGuild;
            channelId = webhookChannel;
            return true;
        }

        guildId = string.Empty;
        channelId = string.Empty;
        return false;
    }

    private void TrackAhelpEmbed(string? messageId, NetUserId userId)
    {
        if (ulong.TryParse(messageId, out var id))
            _ahelpEmbedOwners[id] = userId;
    }

    /// <summary>
    /// Posts or edits a player's ahelp embed through the bot. Returns false if Discord refused it.
    /// </summary>
    private async Task<bool> BotRelayAhelpAsync(DiscordRelayInteraction interaction, WebhookPayload payload)
    {
        var source = payload.Embeds?.FirstOrDefault() ?? new WebhookEmbed();
        var embed = new EmbedProperties
        {
            Author = new EmbedAuthorProperties { Name = payload.Username },
            Description = source.Description,
            Color = new NetCord.Color(source.Color),
            Footer = source.Footer is { } footer
                ? new EmbedFooterProperties { Text = footer.Text, IconUrl = footer.IconUrl }
                : null,
        };

        try
        {
            if (interaction.Id == null)
            {
                var id = await _discordLink.SendMessageGetIdAsync(_botAhelpChannel, new MessageProperties
                {
                    Embeds = [embed],
                    AllowedMentions = AllowedMentionsProperties.None,
                });

                if (id == null)
                    return false;

                interaction.Id = id.Value.ToString();
            }
            else
            {
                await _discordLink.ModifyMessageAsync(_botAhelpChannel, ulong.Parse(interaction.Id), m => m.Embeds = [embed]);
            }

            return true;
        }
        catch (Exception e)
        {
            _sawmill.Error($"The Discord bot couldn't relay an ahelp: {e}");
            return false;
        }
    }

    // Runs on Discord's thread: only checks that need no game state happen here.
    private void OnDiscordMessage(Message message)
    {
        if (message.Author.IsBot || message.MessageReference is not { } reference)
            return;

        var channel = AhelpReplyChannel();
        if (channel == 0 || message.ChannelId != channel)
            return;

        // Staff talking among themselves in the channel: only replies to an ahelp embed are relayed.
        if (message.ReferencedMessage is { } referenced && !referenced.Embeds.Any())
            return;

        _ = HandleDiscordReplyAsync(message, reference.MessageId);
    }

    private async Task HandleDiscordReplyAsync(Message message, ulong repliedTo)
    {
        try
        {
            var member = _ahelpStaffRole == 0 ? null : await _discordLink.GetGuildUserAsync(message.Author.Id);
            if (member == null || !member.RoleIds.Contains(_ahelpStaffRole))
            {
                await _discordLink.AddReactionAsync(message.ChannelId, message.Id, NotStaffReaction);
                return;
            }

            var name = member.Nickname ?? message.Author.GlobalName ?? message.Author.Username;
            var text = message.Content.Trim();
            if (text.Length == 0)
                return;

            _taskManager.RunOnMainThread(() => SendDiscordReply(message.ChannelId, message.Id, repliedTo, name, text));
        }
        catch (Exception e)
        {
            _sawmill.Error($"Couldn't handle an ahelp reply from Discord: {e}");
        }
    }

    private void SendDiscordReply(ulong channelId, ulong messageId, ulong repliedTo, string name, string text)
    {
        if (!_ahelpEmbedOwners.TryGetValue(repliedTo, out var userId))
        {
            React(channelId, messageId, UnknownAhelpReaction);
            return;
        }

        if (text.Length > MessageLengthCap)
            text = text[..MessageLengthCap];

        var escapedText = FormattedMessage.EscapeText(text);
        var discordName = Loc.GetString("serenity-ahelp-discord-name", ("name", FormattedMessage.EscapeText(name)));
        var msg = new BwoinkTextMessage(userId, SystemUserId, $" [color=red]{discordName}[/color]: {escapedText}");

        LogBwoink(msg);
        _activeConversations[userId] = DateTime.Now;

        var admins = GetTargetAdmins();
        foreach (var admin in admins)
        {
            RaiseNetworkEvent(msg, admin);
        }

        var online = _playerManager.TryGetSessionById(userId, out var session);
        if (online && !admins.Contains(session!.Channel))
        {
            var playerMsg = _overrideClientName == string.Empty
                ? msg
                : new BwoinkTextMessage(userId, SystemUserId, $"[color=red]{_overrideClientName}[/color]: {escapedText}");
            RaiseNetworkEvent(playerMsg, session.Channel);
        }

        if (RelaysAhelps)
        {
            var relayParams = new AHelpMessageParams(
                Loc.GetString("serenity-ahelp-discord-name", ("name", name)),
                text,
                true,
                _gameTicker.RoundDuration().ToString("hh\\:mm\\:ss"),
                _gameTicker.RunLevel,
                playedSound: true);
            _messageQueues.GetOrNew(userId).Enqueue(GenerateAHelpMessage(relayParams));
        }

        React(channelId, messageId, online ? DeliveredReaction : OfflineReaction);
    }

    private async void React(ulong channelId, ulong messageId, string emoji)
    {
        try
        {
            await _discordLink.AddReactionAsync(channelId, messageId, emoji);
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Couldn't react to an ahelp reply on Discord: {e.Message}");
        }
    }
}
