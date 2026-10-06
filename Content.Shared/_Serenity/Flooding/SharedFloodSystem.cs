using Content.Shared.Examine;

namespace Content.Shared._Serenity.Flooding;

public abstract partial class SharedFloodSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FloodComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<FloodComponent> ent, ref ExaminedEvent args)
    {
        var depth = ent.Comp.Depth switch
        {
            FloodDepth.Ankles => "flood-examine-depth-ankles",
            FloodDepth.Waist => "flood-examine-depth-waist",
            FloodDepth.Chest => "flood-examine-depth-chest",
            _ => "flood-examine-depth-submerged",
        };

        args.PushMarkup(Loc.GetString(depth));
    }
}
