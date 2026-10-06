using Content.Shared._Serenity.CCVar;
using Content.Shared._Serenity.Flooding;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;

namespace Content.Server._Serenity.Flooding;

public sealed partial class FloodSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;

    private static readonly TimeSpan DrainInterval = TimeSpan.FromSeconds(1);

    private readonly HashSet<Entity<FloodComponent>> _nearDrain = new();
    private float _drainMultiplier;
    private TimeSpan _nextDrain;

    private void InitializeDrains()
    {
        Subs.CVar(_config, SerenityCCVars.FloodDrainMultiplier, v => _drainMultiplier = v, true);
    }

    /// <summary>
    ///     Floor drains swallow flood water around them into their buffer, much faster than they take puddles.
    ///     The drain's own system then empties that buffer and clogs as usual.
    /// </summary>
    private void UpdateDrains(TimeSpan now)
    {
        if (now < _nextDrain)
            return;

        _nextDrain = now + DrainInterval;

        var drains = EntityQueryEnumerator<DrainComponent, TransformComponent>();
        while (drains.MoveNext(out var uid, out var drain, out var xform))
        {
            if (!drain.AutoDrain
                || !xform.Anchored
                || !_solution.TryGetSolution(uid, DrainComponent.SolutionName, out var bufferEnt, out var buffer)
                || buffer.AvailableVolume <= FixedPoint2.Zero)
            {
                continue;
            }

            _nearDrain.Clear();
            _lookup.GetEntitiesInRange(xform.Coordinates, drain.Range, _nearDrain);
            if (_nearDrain.Count == 0)
                continue;

            var share = FixedPoint2.New(drain.UnitsPerSecond * (float) DrainInterval.TotalSeconds * _drainMultiplier / _nearDrain.Count);
            foreach (var flood in _nearDrain)
            {
                if (buffer.AvailableVolume <= FixedPoint2.Zero)
                    break;

                if (!_solution.ResolveSolution(flood.Owner, flood.Comp.Solution, ref flood.Comp.SolutionEntity, out var water))
                    continue;

                var taken = _solution.SplitSolution(flood.Comp.SolutionEntity.Value,
                    FixedPoint2.Min(share, water.Volume, buffer.AvailableVolume));
                _solution.AddSolution(bufferEnt.Value, taken);
            }
        }
    }
}
