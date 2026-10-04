using Content.Shared._Serenity.Stacks;
using Content.Shared.Examine;
using Content.Shared.Item;
using Content.Shared.Stacks;

namespace Content.Shared._Serenity.Economy;

/// <summary>
/// Keeps bound bills with their owner: nobody else can pick them up, and they never merge with another
/// character's. Their stack type already keeps them apart from ordinary bills.
/// </summary>
public abstract partial class SharedBoundCashSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BoundCashComponent, GettingPickedUpAttemptEvent>(OnPickupAttempt);
        SubscribeLocalEvent<BoundCashComponent, StackMergeAttemptEvent>(OnMergeAttempt);
        SubscribeLocalEvent<BoundCashComponent, StackSplitEvent>(OnSplit);
        SubscribeLocalEvent<BoundCashComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>Whether <paramref name="user"/> is the character these bills are bound to.</summary>
    public virtual bool IsOwner(Entity<BoundCashComponent> cash, EntityUid user)
        => cash.Comp.OwnerEntity == user;

    private void OnPickupAttempt(Entity<BoundCashComponent> ent, ref GettingPickedUpAttemptEvent args)
    {
        if (!IsOwner(ent, args.User))
            args.Cancel();
    }

    private void OnMergeAttempt(Entity<BoundCashComponent> ent, ref StackMergeAttemptEvent args)
    {
        if (!TryComp<BoundCashComponent>(args.Donor, out var donor) || donor.ProfileId != ent.Comp.ProfileId)
            args.Cancelled = true;
    }

    private void OnSplit(Entity<BoundCashComponent> ent, ref StackSplitEvent args)
    {
        // The split-off stack is spawned fresh from the prototype; it is still the same owner's money.
        Bind(args.NewId, ent.Comp.ProfileId, ent.Comp.OwnerEntity, ent.Comp.OwnerName);
    }

    private void OnExamined(Entity<BoundCashComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("bound-cash-examine", ("name", ent.Comp.OwnerName)));
    }

    /// <summary>Bind <paramref name="cash"/> to a character.</summary>
    public void Bind(EntityUid cash, int profileId, EntityUid? owner, string ownerName)
    {
        var comp = EnsureComp<BoundCashComponent>(cash);
        comp.ProfileId = profileId;
        comp.OwnerEntity = owner;
        comp.OwnerName = ownerName;
        Dirty(cash, comp);
    }
}
