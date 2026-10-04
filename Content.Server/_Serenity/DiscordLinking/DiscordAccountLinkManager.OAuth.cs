using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web;
using Content.Server.Database;
using Content.Shared._Serenity.CCVar;
using NetCord;
using NetCord.Rest;
using Robust.Server.ServerStatus;
using Robust.Shared.Network;

namespace Content.Server._Serenity.DiscordLinking;

/// <summary>
/// Linking by signing in with the SS14 account, instead of typing a code.
/// <list type="number">
/// <item>The player presses "Link account" in Discord. Discord tells us who they are; we make a one-time state value
/// for that Discord account and answer, privately, with a sign-in link carrying it.</item>
/// <item>They sign in at the SS14 account site, which sends their browser back to <see cref="DiscordOAuth.CallbackPath"/>
/// here with a code and the same state.</item>
/// <item>We swap the code for their SS14 user ID (server to server, with our client secret and the PKCE verifier) and
/// store the link. The state is what ties the SS14 account to the Discord account that pressed the button.</item>
/// </list>
/// </summary>
public sealed partial class DiscordAccountLinkManager
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // Bounds memory if someone presses the button over and over; an honest player never has more than a handful.
    private const int MaxPendingSignIns = 2000;

    private readonly object _lock = new();
    private readonly Dictionary<string, PendingSignIn> _signIns = new();
    private readonly Dictionary<ulong, string> _signInByDiscord = new();

    private int _signInMinutes = 10;
    private string _oauthClientId = string.Empty;
    private string _oauthClientSecret = string.Empty;
    private string _oauthRedirectUri = string.Empty;

    private sealed record PendingSignIn(ulong DiscordId, string DiscordName, string Verifier, DateTime Expires);

    /// <summary>
    /// Whether the OAuth settings are all there and the redirect points at our callback path.
    /// </summary>
    public bool OAuthConfigured
        => _oauthClientId.Length > 0
           && _oauthClientSecret.Length > 0
           && Uri.TryCreate(_oauthRedirectUri, UriKind.Absolute, out var redirect)
           && redirect.AbsolutePath == DiscordOAuth.CallbackPath;

    private void InitializeOAuth()
    {
        _cfg.OnValueChanged(SerenityCCVars.DiscordLinkSignInMinutes, v => _signInMinutes = Math.Max(1, v), true);
        _cfg.OnValueChanged(SerenityCCVars.DiscordLinkOAuthClientId, v => _oauthClientId = v.Trim(), true);
        _cfg.OnValueChanged(SerenityCCVars.DiscordLinkOAuthClientSecret, v => _oauthClientSecret = v.Trim(), true);
        _cfg.OnValueChanged(SerenityCCVars.DiscordLinkOAuthRedirectUri, v => _oauthRedirectUri = v.Trim(), true);

        _statusHost.AddHandler(OnCallbackRequest);
    }

    #region Sign-in state

    /// <summary>
    /// Starts a sign-in for a Discord account and returns the SS14 account site address to send them to. A second press
    /// replaces the first, so each Discord account has at most one live link.
    /// </summary>
    public string BeginSignIn(ulong discordId, string discordName)
    {
        var state = DiscordOAuth.NewState();
        var verifier = DiscordOAuth.NewVerifier();

        lock (_lock)
        {
            PruneExpiredSignIns();

            if (_signInByDiscord.Remove(discordId, out var previous))
                _signIns.Remove(previous);

            // Past the cap, drop the oldest rather than refuse a real player.
            while (_signIns.Count >= MaxPendingSignIns)
            {
                var oldest = _signIns.MinBy(p => p.Value.Expires);
                _signIns.Remove(oldest.Key);
                _signInByDiscord.Remove(oldest.Value.DiscordId);
            }

            _signIns[state] = new PendingSignIn(discordId, discordName, verifier, DateTime.UtcNow.AddMinutes(_signInMinutes));
            _signInByDiscord[discordId] = state;
        }

        return DiscordOAuth.BuildAuthorizeUrl(_oauthClientId, _oauthRedirectUri, state, verifier);
    }

    /// <summary>
    /// Takes the sign-in a state value belongs to, once: the state is spent whether or not the rest of the callback works.
    /// </summary>
    private PendingSignIn? TakeSignIn(string state)
    {
        lock (_lock)
        {
            if (!_signIns.Remove(state, out var pending))
                return null;

            _signInByDiscord.Remove(pending.DiscordId);
            return pending.Expires > DateTime.UtcNow ? pending : null;
        }
    }

    private void PruneExpiredSignIns()
    {
        var now = DateTime.UtcNow;
        foreach (var (state, pending) in _signIns.Where(p => p.Value.Expires <= now).ToList())
        {
            _signIns.Remove(state);
            _signInByDiscord.Remove(pending.DiscordId);
        }
    }

    #endregion

    #region The Discord button

    private async Task HandleLinkButton(ButtonInteraction button)
    {
        var user = button.User;

        if (!OAuthConfigured)
        {
            _sawmill.Error("Someone pressed the link button but the OAuth settings aren't all set.");
            await ReplyPrivately(button, _loc.GetString("serenity-discord-link-reply-not-configured"));
            return;
        }

        // Discord drops interactions not answered within 3 s, so only local work on this path.
        if (await _db.GetDiscordLinkByDiscord(user.Id) is { } existing)
        {
            await ReplyPrivately(button, await DiscordTakenText(existing));
            return;
        }

        var url = BeginSignIn(user.Id, user.Username);
        await button.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties
        {
            Content = _loc.GetString("serenity-discord-link-reply-signin", ("minutes", _signInMinutes)),
            Flags = MessageFlags.Ephemeral,
            AllowedMentions = AllowedMentionsProperties.None,
            Components =
            [
                new ActionRowProperties(
                [
                    new LinkButtonProperties(url, _loc.GetString("serenity-discord-link-signin-button")),
                ]),
            ],
        }));
    }

    private async Task<string> DiscordTakenText(SerenityDiscordLink existing)
    {
        var other = await _db.GetPlayerRecordByUserId(new NetUserId(existing.PlayerUserId));
        return _loc.GetString("serenity-discord-link-reply-discord-taken",
            ("player", other?.LastSeenUserName ?? existing.PlayerUserId.ToString()));
    }

    #endregion

    #region The callback

    private async Task<bool> OnCallbackRequest(IStatusHandlerContext context)
    {
        if (context.RequestMethod != HttpMethod.Get || context.Url.AbsolutePath != DiscordOAuth.CallbackPath)
            return false;

        // The address carries the one-time code and state: don't cache it, and don't leak it in a Referer header.
        context.ResponseHeaders["Cache-Control"] = "no-store";
        context.ResponseHeaders["Referrer-Policy"] = "no-referrer";

        bool success;
        string message;
        try
        {
            (success, message) = await CompleteSignIn(HttpUtility.ParseQueryString(context.Url.Query));
        }
        catch (Exception e)
        {
            _sawmill.Error($"Discord link callback failed: {e}");
            (success, message) = (false, _loc.GetString("serenity-discord-link-web-error"));
        }

        await context.RespondAsync(
            RenderPage(success, message),
            success ? HttpStatusCode.OK : HttpStatusCode.BadRequest,
            "text/html; charset=utf-8");
        return true;
    }

    private async Task<(bool Success, string Message)> CompleteSignIn(System.Collections.Specialized.NameValueCollection query)
    {
        var state = query["state"];
        var pending = string.IsNullOrEmpty(state) ? null : TakeSignIn(state);

        // The player closed or declined the SS14 sign-in page.
        if (!string.IsNullOrEmpty(query["error"]))
            return (false, _loc.GetString("serenity-discord-link-web-cancelled"));

        var code = query["code"];
        if (pending == null || string.IsNullOrEmpty(code))
            return (false, _loc.GetString("serenity-discord-link-web-expired"));

        var identity = await FetchIdentity(code, pending.Verifier);
        if (identity == null)
            return (false, _loc.GetString("serenity-discord-link-web-error"));

        var (userId, userName) = identity.Value;
        if (userName.Length == 0)
            userName = userId.ToString();

        if (await _db.GetDiscordLinkByDiscord(pending.DiscordId) is { } existing)
            return (false, await DiscordTakenText(existing));

        if (!await _db.AddDiscordLink(userId, pending.DiscordId, pending.DiscordName))
            return (false, _loc.GetString("serenity-discord-link-reply-already-linked"));

        _sawmill.Info($"Linked {userName} ({userId}) to Discord {pending.DiscordName} ({pending.DiscordId})");
        await PostLog(LogLinked(pending.DiscordName, pending.DiscordId, userName, userId));
        return (true, _loc.GetString("serenity-discord-link-web-success",
            ("player", userName), ("discordName", pending.DiscordName)));
    }

    /// <summary>
    /// Swaps the code for the player's SS14 user ID and name, asking the SS14 account site directly.
    /// </summary>
    private async Task<(Guid UserId, string UserName)?> FetchIdentity(string code, string verifier)
    {
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, DiscordOAuth.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _oauthRedirectUri,
                ["client_id"] = _oauthClientId,
                ["client_secret"] = _oauthClientSecret,
                ["code_verifier"] = verifier,
            }),
        };

        using var tokenResponse = await Http.SendAsync(tokenRequest);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync();
        if (!tokenResponse.IsSuccessStatusCode || !DiscordOAuth.TryParseAccessToken(tokenBody, out var accessToken))
        {
            // The body is the site's error (e.g. invalid_client), never our secret, but keep the log short anyway.
            _sawmill.Warning($"SS14 account site refused the code: HTTP {(int) tokenResponse.StatusCode} {Truncate(tokenBody)}");
            return null;
        }

        using var infoRequest = new HttpRequestMessage(HttpMethod.Get, DiscordOAuth.UserInfoEndpoint);
        infoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var infoResponse = await Http.SendAsync(infoRequest);
        var infoBody = await infoResponse.Content.ReadAsStringAsync();
        if (!infoResponse.IsSuccessStatusCode || !DiscordOAuth.TryParseUserInfo(infoBody, out var userId, out var userName))
        {
            _sawmill.Warning($"SS14 account site didn't return a usable user: HTTP {(int) infoResponse.StatusCode} {Truncate(infoBody)}");
            return null;
        }

        return (userId, userName);
    }

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200];

    private string RenderPage(bool success, string message)
    {
        var title = _loc.GetString(success ? "serenity-discord-link-web-title-ok" : "serenity-discord-link-web-title-fail");
        var colour = success ? "#3fb950" : "#f85149";
        return $"<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
               + $"<title>{HttpUtility.HtmlEncode(title)}</title>"
               + "<style>body{font-family:system-ui,sans-serif;background:#16181d;color:#e6e6e6;display:flex;min-height:100vh;margin:0;align-items:center;justify-content:center}"
               + "main{max-width:30rem;padding:2rem;margin:1rem;background:#20232a;border-radius:.75rem;border-top:4px solid " + colour + "}"
               + "h1{margin-top:0;font-size:1.4rem}p{line-height:1.5;margin-bottom:0}</style></head><body><main>"
               + $"<h1>{HttpUtility.HtmlEncode(title)}</h1><p>{HttpUtility.HtmlEncode(message)}</p></main></body></html>";
    }

    #endregion
}
