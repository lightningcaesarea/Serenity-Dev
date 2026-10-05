using Content.Shared._Serenity.Plumbing;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Serenity.Plumbing.UI;

[UsedImplicitly]
public sealed class PlumbingPumpBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private PlumbingPumpWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<PlumbingPumpWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;

        _window.OnToggle += enabled => SendMessage(new PlumbingPumpToggleMessage(enabled));
        _window.OnSetRate += rate => SendMessage(new PlumbingPumpSetRateMessage(rate));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not PlumbingPumpBoundUserInterfaceState cast)
            return;

        _window.UpdateState(cast.Enabled, cast.TransferAmount, cast.MaxTransferAmount);
    }
}
