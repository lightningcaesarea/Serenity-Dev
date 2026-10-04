using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace Content.Server._Serenity.DiscordLinking;

/// <summary>
/// The protocol side of "sign in with your SS14 account": the authorization-code flow with PKCE against the SS14 account
/// site. Nothing in here touches Discord or the database, so it can be tested on its own.
/// </summary>
public static class DiscordOAuth
{
    public const string AuthorizeEndpoint = "https://account.spacestation14.com/connect/authorize";
    public const string TokenEndpoint = "https://account.spacestation14.com/connect/token";
    public const string UserInfoEndpoint = "https://account.spacestation14.com/connect/userinfo";

    /// <summary>
    /// The path the SS14 account site sends the player back to. The redirect URI registered there must end in this.
    /// </summary>
    public const string CallbackPath = "/discord-link/callback";

    // 32 random bytes, base64url: 43 characters, inside the 43-128 RFC 7636 allows for a verifier and unguessable for a state.
    private const int SecretBytes = 32;

    public static string NewState() => RandomToken();

    public static string NewVerifier() => RandomToken();

    /// <summary>
    /// The S256 code challenge for a verifier: base64url(SHA-256(verifier)).
    /// </summary>
    public static string Challenge(string verifier)
        => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string BuildAuthorizeUrl(string clientId, string redirectUri, string state, string verifier)
    {
        var query = new StringBuilder();
        Append(query, "client_id", clientId);
        Append(query, "redirect_uri", redirectUri);
        Append(query, "response_type", "code");
        // "openid" gives the stable user ID (sub); "profile" gives the username.
        Append(query, "scope", "openid profile");
        Append(query, "state", state);
        Append(query, "code_challenge", Challenge(verifier));
        Append(query, "code_challenge_method", "S256");
        return $"{AuthorizeEndpoint}?{query}";
    }

    /// <summary>
    /// Reads the SS14 user ID (the <c>sub</c> claim, a GUID) and username out of a userinfo response.
    /// </summary>
    public static bool TryParseUserInfo(string json, out Guid userId, out string userName)
    {
        userId = default;
        userName = string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!root.TryGetProperty("sub", out var sub) || sub.ValueKind != JsonValueKind.String
                || !Guid.TryParse(sub.GetString(), out userId) || userId == Guid.Empty)
            {
                userId = default;
                return false;
            }

            if (root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                userName = name.GetString() ?? string.Empty;
            else if (root.TryGetProperty("preferred_username", out var preferred) && preferred.ValueKind == JsonValueKind.String)
                userName = preferred.GetString() ?? string.Empty;

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the access token out of a token endpoint response.
    /// </summary>
    public static bool TryParseAccessToken(string json, out string accessToken)
    {
        accessToken = string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("access_token", out var token)
                || token.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            accessToken = token.GetString() ?? string.Empty;
            return accessToken.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void Append(StringBuilder query, string key, string value)
    {
        if (query.Length > 0)
            query.Append('&');

        query.Append(key).Append('=').Append(HttpUtility.UrlEncode(value));
    }
}
