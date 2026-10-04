using System;
using System.Web;
using Content.Server._Serenity.DiscordLinking;
using NUnit.Framework;

namespace Content.Tests.Server._Serenity.DiscordLinking;

[TestFixture]
[TestOf(typeof(DiscordOAuth))]
public sealed class DiscordOAuthTest
{
    /// <summary>
    /// The worked example from RFC 7636 appendix B: if our challenge differs, the SS14 account site refuses the code.
    /// </summary>
    [Test]
    public void ChallengeMatchesRfc7636()
    {
        Assert.That(DiscordOAuth.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"),
            Is.EqualTo("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"));
    }

    [Test]
    public void SecretsAreUniqueUrlSafeAndLongEnough()
    {
        var seen = new System.Collections.Generic.HashSet<string>();
        for (var i = 0; i < 50; i++)
        {
            var state = DiscordOAuth.NewState();
            var verifier = DiscordOAuth.NewVerifier();

            Assert.That(seen.Add(state), Is.True, "no repeats");
            Assert.That(seen.Add(verifier), Is.True, "no repeats");
            foreach (var secret in new[] { state, verifier })
            {
                // RFC 7636: a verifier is 43-128 characters of unreserved URL characters.
                Assert.That(secret.Length, Is.InRange(43, 128));
                Assert.That(secret, Does.Match("^[A-Za-z0-9_-]+$"));
            }
        }
    }

    [Test]
    public void AuthorizeUrlCarriesEverythingTheSiteNeeds()
    {
        const string redirect = "https://example.org/discord-link/callback";
        var verifier = DiscordOAuth.NewVerifier();
        var url = DiscordOAuth.BuildAuthorizeUrl("my client", redirect, "the-state", verifier);

        Assert.That(url, Does.StartWith(DiscordOAuth.AuthorizeEndpoint + "?"));
        // Discord refuses a link button longer than 512 characters.
        Assert.That(url.Length, Is.LessThan(512));

        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.That(query["client_id"], Is.EqualTo("my client"));
        Assert.That(query["redirect_uri"], Is.EqualTo(redirect), "the redirect survives the encoding exactly");
        Assert.That(query["response_type"], Is.EqualTo("code"));
        Assert.That(query["scope"], Is.EqualTo("openid profile"));
        Assert.That(query["state"], Is.EqualTo("the-state"));
        Assert.That(query["code_challenge"], Is.EqualTo(DiscordOAuth.Challenge(verifier)));
        Assert.That(query["code_challenge_method"], Is.EqualTo("S256"));
        Assert.That(url, Does.Not.Contain(verifier), "the verifier itself is never in the link");
    }

    [TestCase("{\"sub\":\"11111111-2222-3333-4444-555555555555\",\"name\":\"CoolPlayer\"}", true, "CoolPlayer")]
    [TestCase("{\"sub\":\"11111111-2222-3333-4444-555555555555\",\"preferred_username\":\"Other\"}", true, "Other")]
    [TestCase("{\"sub\":\"11111111-2222-3333-4444-555555555555\"}", true, "")]
    [TestCase("{\"name\":\"NoId\"}", false, "")]
    [TestCase("{\"sub\":\"not-a-guid\",\"name\":\"Bad\"}", false, "")]
    [TestCase("{\"sub\":\"00000000-0000-0000-0000-000000000000\"}", false, "")]
    [TestCase("{\"sub\":12345}", false, "")]
    [TestCase("[]", false, "")]
    [TestCase("not json", false, "")]
    [TestCase("", false, "")]
    public void UserInfoParsing(string json, bool ok, string name)
    {
        Assert.That(DiscordOAuth.TryParseUserInfo(json, out var id, out var parsedName), Is.EqualTo(ok));
        Assert.That(parsedName, Is.EqualTo(name));
        Assert.That(id == Guid.Empty, Is.EqualTo(!ok));
    }

    [TestCase("{\"access_token\":\"abc\",\"token_type\":\"Bearer\"}", true, "abc")]
    [TestCase("{\"access_token\":\"\"}", false, "")]
    [TestCase("{\"error\":\"invalid_grant\"}", false, "")]
    [TestCase("{\"access_token\":5}", false, "")]
    [TestCase("nope", false, "")]
    public void AccessTokenParsing(string json, bool ok, string token)
    {
        Assert.That(DiscordOAuth.TryParseAccessToken(json, out var parsed), Is.EqualTo(ok));
        Assert.That(parsed, Is.EqualTo(token));
    }
}
