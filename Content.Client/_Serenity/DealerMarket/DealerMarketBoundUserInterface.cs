using Content.Shared._Serenity.DealerMarket;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Serenity.DealerMarket;

[UsedImplicitly]
public sealed class DealerMarketBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private DealerMarketWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<DealerMarketWindow>();
        _window.OnFulfil += id => SendMessage(new DealerFulfilMessage(id));
        _window.OnRequestCrate += id => SendMessage(new DealerRequestCrateMessage(id));
        _window.OnDecline += id => SendMessage(new DealerDeclineMessage(id));
        _window.OnBuy += (dealer, offer) => SendMessage(new DealerBuyMessage(dealer, offer));
        _window.OnSell += dealer => SendMessage(new DealerSellMessage(dealer));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (message is DealerMarketStateMessage state)
            _window?.Update(state);
    }
}
