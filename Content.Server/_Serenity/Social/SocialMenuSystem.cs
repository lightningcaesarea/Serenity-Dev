using Content.Shared._Serenity.Social;
using Robust.Shared.Player;

namespace Content.Server._Serenity.Social;

public sealed partial class SocialMenuSystem : SharedSocialMenuSystem
{
    protected override bool IsPlayer(EntityUid user)
    {
        return HasComp<ActorComponent>(user);
    }
}
