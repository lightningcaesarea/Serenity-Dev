using Content.Shared._Serenity.Intimacy;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Serenity.Intimacy.UI;

[UsedImplicitly]
public sealed class MilkingMachineBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private MilkingMachineWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<MilkingMachineWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.OnSetMode += mode => SendMessage(new MilkingMachineSetModeMessage(mode));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not MilkingMachineUiState cast)
            return;

        _window.UpdateState(cast);
    }
}
