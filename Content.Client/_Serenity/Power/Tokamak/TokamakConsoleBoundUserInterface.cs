using Content.Shared._Serenity.Power.Tokamak;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Serenity.Power.Tokamak;

[UsedImplicitly]
public sealed class TokamakConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private TokamakConsoleWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<TokamakConsoleWindow>();

        _window.ActiveToggled += active => SendMessage(new TokamakSetActiveMessage(active));
        _window.FieldStrengthChanged += strength => SendMessage(new TokamakSetFieldStrengthMessage(strength));
        _window.ScramPressed += () => SendMessage(new TokamakScramMessage());
        _window.DeviceSettingChanged += (device, setting, value) =>
            SendMessage(new TokamakDeviceSettingMessage(device, setting, value));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is TokamakConsoleBuiState cast)
            _window?.Update(cast);
    }
}
