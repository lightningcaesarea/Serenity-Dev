## Connection refusals

serenity-discord-link-deny-unlinked =
    This server requires your SS14 account to be linked to Discord.

    1. Join our Discord: {$invite}
    2. Find the verification channel and press "Link account".
    3. Press "Sign in with SS14" and sign in with this same Space Station 14 account.
    4. Reconnect.
serenity-discord-link-deny-not-member =
    Your linked Discord account isn't in our Discord server.
    Rejoin to play: {$invite}
serenity-discord-link-deny-banned = Your linked Discord account is banned from our Discord server, so you can't join the game.
serenity-discord-link-deny-unavailable = We can't reach Discord to verify your account right now. Please try again in a few minutes.
serenity-discord-link-no-invite = ask a staff member for an invite
serenity-discord-link-kick-removed = Your linked Discord account left or was removed from our Discord server.

## Discord bot

serenity-discord-link-panel-text = **Link your SS14 account.** Press the button below, then sign in with your Space Station 14 account. You never type a password here: the sign-in happens on the official SS14 account site, and only you will see the link.
serenity-discord-link-panel-button = Link account
serenity-discord-link-reply-signin = Press the button below and sign in with the Space Station 14 account you play on. This link is only for you and works for {$minutes} minutes.
serenity-discord-link-signin-button = Sign in with SS14
serenity-discord-link-reply-not-configured = Linking isn't set up yet. Please tell a staff member.
serenity-discord-link-reply-discord-taken = This Discord account is already linked to SS14 account **{$player}**. Ask staff if you need that changed.
serenity-discord-link-reply-already-linked = That SS14 account is already linked. Ask staff if you need that changed.
serenity-discord-link-log-linked = Linked: Discord **{$discordName}** (`{$discordId}`) <-> SS14 **{$player}** (`{$userId}`)
serenity-discord-link-log-removed = Left or was removed from the Discord while linked: Discord **{$discordName}** (`{$discordId}`) <-> SS14 **{$player}** (`{$userId}`)
serenity-discord-link-log-unlinked = Unlinked by **{$by}** (`{$byId}`): {$link}
serenity-discord-link-web-title-ok = Account linked
serenity-discord-link-web-title-fail = Couldn't link your account
serenity-discord-link-web-success = SS14 account {$player} is now linked to Discord account {$discordName}. You can close this tab and join the game.
serenity-discord-link-web-cancelled = The sign-in was cancelled, so nothing was linked. Press "Link account" in Discord to try again.
serenity-discord-link-web-expired = This sign-in link is invalid, expired or was already used. Press "Link account" in Discord to get a new one.
serenity-discord-link-web-error = Something went wrong talking to the SS14 account site, so nothing was linked. Press "Link account" in Discord to try again, and tell a staff member if it keeps happening.
serenity-discord-link-whois-usage = Usage: {$prefix}whois <@user | Discord ID | SS14 username>
serenity-discord-link-unlink-usage = Usage: {$prefix}unlink <@user | Discord ID | SS14 username>
serenity-discord-link-whois-none = No link found.
serenity-discord-link-unlinked = Removed link: {$link}
serenity-discord-link-describe = SS14 {$player} ({$userId}) <-> Discord {$discordName} ({$discordId}), linked {$linkedAt} UTC

## Admin commands

cmd-discordlink_info-desc = Shows which Discord account a player is linked to.
cmd-discordlink_info-help = Usage: discordlink_info <username or user ID>
cmd-discordlink_lookup-desc = Finds the SS14 account linked to a Discord user ID.
cmd-discordlink_lookup-help = Usage: discordlink_lookup <Discord user ID>
cmd-discordlink_remove-desc = Removes a player's Discord link so they can link a different account.
cmd-discordlink_remove-help = Usage: discordlink_remove <username or user ID>
serenity-discord-link-cmd-no-player = Couldn't find a player called {$player}.
serenity-discord-link-cmd-not-linked = {$player} has no linked Discord account.
serenity-discord-link-cmd-discord-not-linked = Discord user {$discordId} isn't linked to any SS14 account.
serenity-discord-link-cmd-hint-player = <username or user ID>
