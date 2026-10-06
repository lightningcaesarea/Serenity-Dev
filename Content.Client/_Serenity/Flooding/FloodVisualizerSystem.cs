using Content.Shared._Serenity.Flooding;
using Robust.Client.GameObjects;

namespace Content.Client._Serenity.Flooding;

/// <summary>
///     Picks the flood sprite for how deep the water is and tints it the colour of the liquid.
/// </summary>
public sealed partial class FloodVisualizerSystem : VisualizerSystem<FloodVisualsComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, FloodVisualsComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } sprite
            || !SpriteSystem.LayerMapTryGet((uid, sprite), FloodVisualLayers.Liquid, out var layer, false))
        {
            return;
        }

        if (AppearanceSystem.TryGetData<FloodDepth>(uid, FloodVisuals.Depth, out var depth, args.Component)
            && component.States.TryGetValue(depth, out var state))
        {
            SpriteSystem.LayerSetRsiState((uid, sprite), layer, state);
        }

        if (AppearanceSystem.TryGetData<Color>(uid, FloodVisuals.Color, out var color, args.Component))
            SpriteSystem.LayerSetColor((uid, sprite), layer, color.WithAlpha(component.Alpha));
    }
}
