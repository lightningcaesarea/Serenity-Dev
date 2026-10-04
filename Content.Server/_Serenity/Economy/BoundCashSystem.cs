using Content.Shared._Serenity.Economy;
using Robust.Server.Player;

namespace Content.Server._Serenity.Economy;

public sealed partial class BoundCashSystem : SharedBoundCashSystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private ICharacterBalanceManager _balances = default!;

    /// <summary>The server also knows who is playing which character, so a new body (cloned, borged) still counts.</summary>
    public override bool IsOwner(Entity<BoundCashComponent> cash, EntityUid user)
    {
        if (base.IsOwner(cash, user))
            return true;

        return _players.TryGetSessionByEntity(user, out var session)
            && _balances.GetActiveProfileId(session.UserId) == cash.Comp.ProfileId;
    }
}
