using Content.Shared._Serenity.Flooding;
using Robust.Client.GameObjects;

namespace Content.Client._Serenity.Flooding;

/// <summary>
///     Tints the flood the colour of the liquid, more opaque the deeper it is.
/// </summary>
public sealed partial class FloodVisualizerSystem : VisualizerSystem<FloodVisualsComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, FloodVisualsComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } sprite
            || !SpriteSystem.LayerMapTryGet((uid, sprite), FloodVisualLayers.Liquid, out var layer, false)
            || !AppearanceSystem.TryGetData<Color>(uid, FloodVisuals.Color, out var color, args.Component))
        {
            return;
        }

        var alpha = AppearanceSystem.TryGetData<FloodDepth>(uid, FloodVisuals.Depth, out var depth, args.Component)
            && component.Alpha.TryGetValue(depth, out var depthAlpha)
                ? depthAlpha
                : component.DefaultAlpha;

        SpriteSystem.LayerSetColor((uid, sprite), layer, color.WithAlpha(alpha));
    }
}
