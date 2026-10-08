// Ported from Frontier Station 14 at commit 5be37d18c2 (2024-07-01), MIT licensed.
// Copyright (c) 2017-2024 New Frontiers. Adapted for Serenity.

using Content.Client._Serenity.Shipyard.UI;
using Content.Shared._Serenity.Shipyard.BUI;
using Content.Shared._Serenity.Shipyard.Components;
using Content.Shared._Serenity.Shipyard.Events;
using Content.Shared.Containers.ItemSlots;
using Robust.Client.UserInterface;

namespace Content.Client._Serenity.Shipyard.BUI;

public sealed class ShipyardConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private ShipyardConsoleMenu? _menu;

    public ShipyardConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<ShipyardConsoleMenu>();
        _menu.OnPurchase += id => SendMessage(new ShipyardConsolePurchaseMessage(id));
        _menu.OnSellShip += () => SendMessage(new ShipyardConsoleSellMessage());
        _menu.OnSaveShip += () => SendMessage(new ShipyardConsoleSaveMessage()); // Serenity
        _menu.OnLoadShip += id => SendMessage(new ShipyardConsoleLoadMessage(id)); // Serenity
        _menu.OnToggleId += () => SendMessage(new ItemSlotButtonPressedEvent(ShipyardConsoleComponent.TargetIdCardSlotId));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not ShipyardConsoleInterfaceState cState)
            return;

        _menu?.UpdateState(cState);
    }
}
