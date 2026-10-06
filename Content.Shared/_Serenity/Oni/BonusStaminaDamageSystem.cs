using Content.Shared.Damage.Components;
using Content.Shared.Damage.Events;

namespace Content.Shared._Serenity.Oni;

public sealed class BonusStaminaDamageSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StaminaDamageOnHitComponent, StaminaMeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(Entity<StaminaDamageOnHitComponent> ent, ref StaminaMeleeHitEvent args)
    {
        if (TryComp<BonusStaminaDamageComponent>(args.User, out var bonus))
            args.Multiplier *= bonus.Multiplier;
    }
}
