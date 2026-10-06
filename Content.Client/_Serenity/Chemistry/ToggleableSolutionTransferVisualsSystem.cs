using Content.Shared._Serenity.Chemistry;
using Content.Shared._Serenity.Plumbing;
using Robust.Client.GameObjects;

namespace Content.Client._Serenity.Chemistry;

/// <summary>
/// Draws a tank's mode light: one colour while it is filling, another while it is dispensing.
/// While the tank is docked on a fluid connector port the light shows the plumbing valve instead:
/// the filling colour for Fill, the dispensing colour for Supply, and off for Closed.
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
        var lit = true;

        if (AppearanceSystem.TryGetData<bool>(uid, PlumbingPortableVisuals.Docked, out var docked, args.Component)
            && docked
            && AppearanceSystem.TryGetData<PlumbingPortableMode>(uid, PlumbingPortableVisuals.Mode, out var valve, args.Component))
        {
            lit = valve != PlumbingPortableMode.Closed;
            filling = valve == PlumbingPortableMode.Fill;
        }

        SpriteSystem.LayerSetVisible((uid, sprite), layer, lit);
        SpriteSystem.LayerSetColor((uid, sprite), layer,
            filling ? component.FillingLightColor : component.DispensingLightColor);
    }
}
