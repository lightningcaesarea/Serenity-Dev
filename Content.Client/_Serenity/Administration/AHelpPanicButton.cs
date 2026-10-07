using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared._Serenity.Administration;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._Serenity.Administration;

/// <summary>
/// The panic button in the player's ahelp window. It opens a warning that has to be confirmed
/// before anything is sent.
/// </summary>
public sealed partial class AHelpPanicButton : Button
{
    [Dependency] private IEntityNetworkManager _net = default!;

    private DefaultWindow? _confirmWindow;

    public AHelpPanicButton()
    {
        IoCManager.InjectDependencies(this);

        Text = Loc.GetString("ahelp-panic-button");
        StyleClasses.Add(StyleNano.ButtonCaution);
        HorizontalAlignment = HAlignment.Right;
        OnPressed += _ => OpenConfirm();
    }

    private void OpenConfirm()
    {
        if (_confirmWindow is { Disposed: false })
        {
            _confirmWindow.OpenCentered();
            return;
        }

        var warning = new RichTextLabel { HorizontalExpand = true, VerticalExpand = true };
        warning.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("ahelp-panic-warning")));

        var confirm = new Button
        {
            Text = Loc.GetString("ahelp-panic-confirm"),
            HorizontalExpand = true,
        };
        confirm.StyleClasses.Add(StyleNano.ButtonCaution);

        var cancel = new Button
        {
            Text = Loc.GetString("ahelp-panic-cancel"),
            HorizontalExpand = true,
        };

        var buttons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
        };
        buttons.AddChild(cancel);
        buttons.AddChild(confirm);

        var contents = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
        };
        contents.AddChild(warning);
        contents.AddChild(buttons);

        var window = new DefaultWindow
        {
            Title = Loc.GetString("ahelp-panic-title"),
            MinSize = new Vector2(420, 200),
        };
        window.Contents.AddChild(contents);
        _confirmWindow = window;

        cancel.OnPressed += _ => window.Close();
        confirm.OnPressed += _ =>
        {
            _net.SendSystemNetworkMessage(new AHelpPanicButtonEvent());
            window.Close();
        };

        window.OpenCentered();
    }
}
