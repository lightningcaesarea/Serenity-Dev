using Content.Server._Starlight.Medical.Body.Systems;
using Content.Server.Body.Components;
using Content.Shared._Serenity.Flooding;
using Content.Shared.Atmos;
using Content.Shared.Standing;
using Robust.Shared.Map.Components;

namespace Content.Server._Serenity.Flooding;

public sealed partial class FloodSystem
{
    [Dependency] private StandingStateSystem _standing = default!;

    private void InitializeBreathing()
    {
        // Internals and anything an entity is sealed inside of pick the air first; the flood only takes over
        // when the breath would otherwise come from the room.
        SubscribeLocalEvent<RespiratorComponent, InhaleLocationEvent>(OnInhale, after: [typeof(InternalsSystem)]);
        SubscribeLocalEvent<RespiratorComponent, ExhaleLocationEvent>(OnExhale);
    }

    private void OnInhale(Entity<RespiratorComponent> ent, ref InhaleLocationEvent args)
    {
        if (args.Gas != null || !IsUnderwater(ent))
            return;

        // A lungful of water: no air at all.
        args.Gas = new GasMixture(args.Respirator.BreathVolume);
    }

    private void OnExhale(Entity<RespiratorComponent> ent, ref ExhaleLocationEvent args)
    {
        if (args.Gas != null || !IsUnderwater(ent))
            return;

        // Bubbles, not room air.
        args.Gas = new GasMixture();
    }

    /// <summary>
    ///     Whether the water on this entity's tile is over its head: a full flood for anyone standing,
    ///     or waist deep for anyone lying down in it.
    /// </summary>
    public bool IsUnderwater(EntityUid uid)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } gridUid
            || xform.ParentUid != gridUid
            || !TryComp(gridUid, out MapGridComponent? grid)
            || !TryGetFlood(gridUid, grid, _map.TileIndicesFor(gridUid, grid, xform.Coordinates), out var flood))
        {
            return false;
        }

        return flood.Comp.Depth switch
        {
            FloodDepth.Submerged => true,
            FloodDepth.Chest or FloodDepth.Waist => _standing.IsDown(uid),
            _ => false,
        };
    }
}
