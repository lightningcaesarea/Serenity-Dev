using Content.Client.Chemistry.Visualizers;
using Content.Shared._Serenity.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.SprayPainter.Prototypes;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._Serenity.Chemistry;

/// <summary>
/// Redraws a spray painted storage tank as the tank it was painted to look like: body state and fill levels.
/// </summary>
public sealed partial class PaintableTankVisualsSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IComponentFactory _compFactory = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        // The fill layer has to be resized before the solution visuals draw it.
        SubscribeLocalEvent<PaintableTankVisualsComponent, AppearanceChangeEvent>(OnAppearanceChange,
            before: [typeof(SolutionContainerVisualsSystem)]);
    }

    private void OnAppearanceChange(Entity<PaintableTankVisualsComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } sprite
            || !_appearance.TryGetData<string>(ent, PaintableVisuals.Prototype, out var protoId, args.Component)
            || !_proto.TryIndex(protoId, out var proto)
            || !proto.TryGetComponent(out PaintableTankVisualsComponent? style, _compFactory))
        {
            return;
        }

        _sprite.LayerSetRsiState((ent.Owner, sprite), 0, style.BaseState);

        if (TryComp<SolutionContainerVisualsComponent>(ent, out var fill)
            && proto.TryGetComponent(out SolutionContainerVisualsComponent? styleFill, _compFactory))
        {
            fill.FillBaseName = styleFill.FillBaseName;
            fill.MaxFillLevels = styleFill.MaxFillLevels;
        }
    }
}
