using Content.Client.Verbs.UI;
using Content.Shared._Serenity.Social;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._Serenity.Social;

public sealed partial class SocialMenuSystem : SharedSocialMenuSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    protected override bool IsPlayer(EntityUid user)
    {
        // The client only predicts its own player's clicks.
        return _player.LocalEntity == user;
    }

    protected override void OpenMenu(EntityUid user, EntityUid target)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        _ui.GetUIController<VerbMenuUIController>().OpenSocialMenu(target);
    }
}
