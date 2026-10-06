using Content.Shared.Procedural;
using Robust.Shared.Prototypes;

namespace Content.Server._Serenity.Worldgen;

/// <summary>
/// Marks a worldgen debris grid whose contents come from a <see cref="DungeonConfigPrototype"/> instead of
/// a blob floor plan. The dungeon is generated on the debris grid when it is spawned.
/// </summary>
[RegisterComponent]
public sealed partial class DungeonDebrisComponent : Component
{
    [DataField(required: true)]
    public ProtoId<DungeonConfigPrototype> Config;
}
