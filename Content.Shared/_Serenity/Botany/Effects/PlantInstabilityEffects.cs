using Content.Shared.EntityEffects;
using Content.Shared.EntityEffects.Effects.Botany.PlantAttributes;
using Robust.Shared.Prototypes;
using Content.Shared.Botany.Systems;

namespace Content.Shared._Serenity.Botany.Effects;

/// <summary>
/// Entity effect that raises or lowers a plant's instability.
/// </summary>
public sealed partial class PlantAdjustInstabilityEntityEffectSystem : EntityEffectSystem<PlantInstabilityComponent, PlantAdjustInstability>
{
    [Dependency] private PlantHolderSystem _holder = default!;
    [Dependency] private PlantInstabilitySystem _instability = default!;

    protected override void Effect(Entity<PlantInstabilityComponent> entity, ref EntityEffectEvent<PlantAdjustInstability> args)
    {
        if (_holder.IsDead(entity.Owner))
            return;

        _instability.AdjustInstability(entity.AsNullable(), args.Effect.Amount);
    }
}

/// <inheritdoc cref="EntityEffect"/>
public sealed partial class PlantAdjustInstability : BasePlantAdjustAttribute<PlantAdjustInstability>
{
    public override string GuidebookAttributeName { get; set; } = "plant-attribute-instability";

    public override bool GuidebookIsAttributePositive { get; protected set; } = false;
}

/// <summary>
/// Entity effect for mutagenic reagents: rolls a random outcome on the plant, see
/// <see cref="PlantInstabilitySystem.RollMutagenOutcome"/>.
/// </summary>
public sealed partial class PlantMutagenOutcomeEntityEffectSystem : EntityEffectSystem<PlantInstabilityComponent, PlantMutagenOutcome>
{
    [Dependency] private PlantInstabilitySystem _instability = default!;

    protected override void Effect(Entity<PlantInstabilityComponent> entity, ref EntityEffectEvent<PlantMutagenOutcome> args)
    {
        _instability.RollMutagenOutcome(entity);
    }
}

/// <inheritdoc cref="EntityEffect"/>
public sealed partial class PlantMutagenOutcome : EntityEffectBase<PlantMutagenOutcome>
{
    public override string? EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys, ILocalizationManager loc) =>
        loc.GetString("entity-effect-guidebook-plant-mutagen-outcome", ("chance", Probability));
}
