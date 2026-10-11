using Content.Shared._Serenity.Botany;
using Robust.Client.UserInterface;

namespace Content.Client._Serenity.Botany;

public sealed partial class PlantAnalyzerBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private PlantAnalyzerWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<PlantAnalyzerWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window != null && state is PlantAnalyzerUiState cast)
            _window.Populate(cast);
    }
}
