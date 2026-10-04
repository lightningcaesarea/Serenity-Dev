using System.Web;
using Content.Server._Serenity.DiscordLinking;
using Content.Shared._Serenity.CCVar;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._Serenity.DiscordLinking;

[TestFixture]
public sealed class DiscordLinkSignInTest
{
    private const string Redirect = "https://example.org/discord-link/callback";

    /// <summary>
    /// Linking only works with all three OAuth settings, and a redirect that ends in the callback path the server answers
    /// on; anything else would send players to a page that can't finish the link. Until then the bot says it isn't set up.
    /// </summary>
    [Test]
    public async Task OAuthNeedsClientSecretAndTheRightRedirect()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Server.WaitAssertion(() =>
        {
            var cfg = pair.Server.ResolveDependency<IConfigurationManager>();
            var links = pair.Server.ResolveDependency<DiscordAccountLinkManager>();

            Assert.That(links.OAuthConfigured, Is.False, "nothing is set by default");

            cfg.SetCVar(SerenityCCVars.DiscordLinkOAuthClientId, "client");
            cfg.SetCVar(SerenityCCVars.DiscordLinkOAuthClientSecret, "secret");
            cfg.SetCVar(SerenityCCVars.DiscordLinkOAuthRedirectUri, "https://example.org/somewhere-else");
            Assert.That(links.OAuthConfigured, Is.False, "the redirect must end in the callback path");

            cfg.SetCVar(SerenityCCVars.DiscordLinkOAuthRedirectUri, Redirect);
            Assert.That(links.OAuthConfigured, Is.True);

            cfg.SetCVar(SerenityCCVars.DiscordLinkOAuthClientSecret, "");
            Assert.That(links.OAuthConfigured, Is.False, "no secret, no linking");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The link the button hands out names our client and callback and carries a fresh state each time, so one Discord
    /// account's link can't be used to link another.
    /// </summary>
    [Test]
    public async Task SignInLinksAreFreshPerPress()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Server.WaitAssertion(() =>
        {
            var cfg = pair.Server.ResolveDependency<IConfigurationManager>();
            var links = pair.Server.ResolveDependency<DiscordAccountLinkManager>();

            cfg.SetCVar(SerenityCCVars.DiscordLinkOAuthClientId, "client");
            cfg.SetCVar(SerenityCCVars.DiscordLinkOAuthClientSecret, "secret");
            cfg.SetCVar(SerenityCCVars.DiscordLinkOAuthRedirectUri, Redirect);

            var first = HttpUtility.ParseQueryString(new Uri(links.BeginSignIn(1, "one")).Query);
            var again = HttpUtility.ParseQueryString(new Uri(links.BeginSignIn(1, "one")).Query);
            var other = HttpUtility.ParseQueryString(new Uri(links.BeginSignIn(2, "two")).Query);

            Assert.That(first["client_id"], Is.EqualTo("client"));
            Assert.That(first["redirect_uri"], Is.EqualTo(Redirect));
            Assert.That(first["state"], Is.Not.EqualTo(again["state"]), "pressing again makes a new link");
            Assert.That(again["state"], Is.Not.EqualTo(other["state"]), "each Discord account has its own");
            Assert.That(first["code_challenge"], Is.Not.EqualTo(again["code_challenge"]), "and its own PKCE secret");
        });

        await pair.CleanReturnAsync();
    }
}
