using Content.Shared._Serenity.Chemistry;
using Robust.Client.GameObjects;

namespace Content.Client._Serenity.Chemistry;

/// <summary>
/// Draws a tank's mode light: one colour while it is filling, another while it is dispensing.
/// </summary>
public sealed partial class ToggleableSolutionTransferVisualsSystem : VisualizerSystem<ToggleableSolutionTransferComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, ToggleableSolutionTransferComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } sprite || component.LightState is not { } lightState)
            return;

        if (component.LightBezelState is { } bezelState
            && !SpriteSystem.LayerMapTryGet((uid, sprite), ToggleableSolutionTransferLayers.LightBezel, out _, false))
        {
            var bezel = SpriteSystem.LayerMapReserve((uid, sprite), ToggleableSolutionTransferLayers.LightBezel);
            SpriteSystem.LayerSetRsiState((uid, sprite), bezel, bezelState);
        }

        if (!SpriteSystem.LayerMapTryGet((uid, sprite), ToggleableSolutionTransferLayers.Light, out var layer, false))
        {
            layer = SpriteSystem.LayerMapReserve((uid, sprite), ToggleableSolutionTransferLayers.Light);
            SpriteSystem.LayerSetRsiState((uid, sprite), layer, lightState);
            sprite.LayerSetShader(layer, "unshaded");
        }

        var filling = AppearanceSystem.TryGetData<bool>(uid, ToggleableSolutionTransferVisuals.Filling, out var data, args.Component)
            ? data
            : component.Filling;

        SpriteSystem.LayerSetColor((uid, sprite), layer,
            filling ? component.FillingLightColor : component.DispensingLightColor);
    }
}
