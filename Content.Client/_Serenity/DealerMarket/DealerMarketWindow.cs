using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Serenity.DealerMarket;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Serenity.DealerMarket;

/// <summary>
/// The dealer market terminal: a row of dealers across the top, and under the chosen one their greeting, the
/// contracts they have for this player, what they sell and what they will take off the intake pad.
/// </summary>
public sealed partial class DealerMarketWindow : FancyWindow
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IResourceCache _cache = default!;

    public event Action<int>? OnFulfil;
    public event Action<int>? OnDecline;
    public event Action<ProtoId<DealerPrototype>, int>? OnBuy;
    public event Action<ProtoId<DealerPrototype>>? OnSell;

    private readonly Label _balance = new();
    private readonly Label _status = new() { StyleClasses = { "LabelSubText" } };
    private readonly BoxContainer _dealerRow = new() { Orientation = BoxContainer.LayoutOrientation.Horizontal };
    private readonly BoxContainer _body = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true };

    private ProtoId<DealerPrototype>? _selected;
    private DealerMarketStateMessage? _state;

    public DealerMarketWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("dealer-market-title");
        MinSize = new Vector2(620, 520);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8) };
        root.AddChild(_balance);
        root.AddChild(_status);
        root.AddChild(_dealerRow);
        root.AddChild(new PanelContainer { MinHeight = 2, PanelOverride = new Robust.Client.Graphics.StyleBoxFlat(Color.Gray) });
        root.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _body } });
        ContentsContainer.AddChild(root);
    }

    public void Update(DealerMarketStateMessage state)
    {
        _state = state;

        var dealers = _proto.EnumeratePrototypes<DealerPrototype>().OrderBy(d => d.Name).ToList();
        if (_selected == null || !dealers.Any(d => d.ID == _selected))
            _selected = dealers.FirstOrDefault()?.ID;

        _balance.Text = Loc.GetString("dealer-market-balance", ("balance", state.Balance));
        _status.Text = Loc.GetString("dealer-market-status",
            ("intake", Loc.GetString(state.HasIntake ? "dealer-market-linked" : "dealer-market-not-linked")),
            ("outlet", Loc.GetString(state.HasOutlet ? "dealer-market-linked" : "dealer-market-not-linked")),
            ("incoming", state.Incoming));

        _dealerRow.RemoveAllChildren();
        foreach (var dealer in dealers)
        {
            var id = dealer.ID;
            var button = new Button
            {
                ToggleMode = true,
                Pressed = id == _selected,
                Text = dealer.Name,
                MinWidth = 110,
                Margin = new Thickness(2),
                // Contracts waiting on this dealer show as a count on the tab.
                ToolTip = dealer.Trade,
            };
            var waiting = state.Contracts.Count(c => c.Dealer == id);
            if (waiting > 0)
                button.Text = $"{dealer.Name} ({waiting})";

            button.OnPressed += _ =>
            {
                _selected = id;
                Update(_state!);
            };
            _dealerRow.AddChild(button);
        }

        _body.RemoveAllChildren();
        if (_selected is { } current)
            Populate(_proto.Index(current), state);
    }

    private void Populate(DealerPrototype dealer, DealerMarketStateMessage state)
    {
        var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 6) };
        header.AddChild(new TextureRect
        {
            Texture = _cache.GetResource<TextureResource>(dealer.Portrait).Texture,
            TextureScale = new Vector2(3, 3),
            Stretch = TextureRect.StretchMode.Keep,
            MinSize = new Vector2(112, 112),
        });

        var intro = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(10, 0) };
        intro.AddChild(new Label { Text = dealer.Name, StyleClasses = { "LabelHeading" } });
        intro.AddChild(new Label { Text = dealer.Trade, StyleClasses = { "LabelSubText" } });
        intro.AddChild(Wrapped($"\"{dealer.Greeting}\"", 380));
        header.AddChild(intro);
        _body.AddChild(header);

        AddHeading(Loc.GetString("dealer-market-contracts"));
        var contracts = state.Contracts.Where(c => c.Dealer == dealer.ID).ToList();
        if (contracts.Count == 0)
            _body.AddChild(new Label { Text = Loc.GetString("dealer-market-no-contracts"), StyleClasses = { "LabelSubText" } });

        foreach (var contract in contracts)
        {
            _body.AddChild(ContractBox(contract));
        }

        if (dealer.Stock.Count > 0)
        {
            AddHeading(Loc.GetString("dealer-market-stock"));
            for (var i = 0; i < dealer.Stock.Count; i++)
            {
                var index = i;
                var offer = dealer.Stock[i];
                var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
                var name = _proto.Index(offer.Item).Name;
                row.AddChild(new Label
                {
                    Text = offer.Amount > 1 ? $"{name} x{offer.Amount}" : name,
                    HorizontalExpand = true,
                });
                var buy = new Button
                {
                    Text = Loc.GetString("dealer-market-buy", ("price", offer.Price)),
                    Disabled = state.Balance < offer.Price || !state.HasOutlet,
                };
                buy.OnPressed += _ => OnBuy?.Invoke(dealer.ID, index);
                row.AddChild(buy);
                _body.AddChild(row);
            }
        }

        if (dealer.Buys != null)
        {
            AddHeading(Loc.GetString("dealer-market-sell"));
            state.Quotes.TryGetValue(dealer.ID, out var quote);
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            row.AddChild(new Label
            {
                Text = Loc.GetString("dealer-market-sell-blurb", ("rate", (int) (dealer.BuyRate * 100))),
                HorizontalExpand = true,
                StyleClasses = { "LabelSubText" },
            });
            var sell = new Button
            {
                Text = Loc.GetString("dealer-market-sell-button", ("payout", quote)),
                Disabled = quote <= 0,
            };
            sell.OnPressed += _ => OnSell?.Invoke(dealer.ID);
            row.AddChild(sell);
            _body.AddChild(row);
        }

        AddHeading(Loc.GetString("dealer-market-pad"));
        if (state.Pad.Count == 0)
            _body.AddChild(new Label { Text = Loc.GetString("dealer-market-pad-empty"), StyleClasses = { "LabelSubText" } });
        foreach (var item in state.Pad)
        {
            _body.AddChild(new Label { Text = $"{item.Name} x{item.Count}" });
        }
    }

    private Control ContractBox(DealerContractInfo contract)
    {
        var box = new PanelContainer
        {
            PanelOverride = new Robust.Client.Graphics.StyleBoxFlat(Color.FromHex("#25252a")),
            Margin = new Thickness(0, 3),
        };
        var inner = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(6) };
        box.AddChild(inner);

        var title = contract.RoleContract
            ? Loc.GetString("dealer-market-role-contract", ("title", contract.Title))
            : contract.Title;
        inner.AddChild(new Label { Text = title, StyleClasses = { "LabelHeading" } });
        inner.AddChild(Wrapped(contract.Flavour, 520));

        foreach (var want in contract.Wants)
        {
            inner.AddChild(new Label
            {
                Text = Loc.GetString("dealer-market-want", ("name", want.Name), ("onPad", want.OnPad), ("wanted", want.Wanted)),
                FontColorOverride = want.OnPad >= want.Wanted ? Color.LightGreen : null,
            });
        }

        var footer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        footer.AddChild(new Label
        {
            Text = Loc.GetString("dealer-market-contract-footer",
                ("payout", contract.Payout), ("minutes", (contract.SecondsLeft + 59) / 60)),
            HorizontalExpand = true,
        });
        var decline = new Button { Text = Loc.GetString("dealer-market-decline") };
        decline.OnPressed += _ => OnDecline?.Invoke(contract.Id);
        footer.AddChild(decline);
        var fulfil = new Button { Text = Loc.GetString("dealer-market-fulfil"), Disabled = !contract.Ready };
        fulfil.OnPressed += _ => OnFulfil?.Invoke(contract.Id);
        footer.AddChild(fulfil);
        inner.AddChild(footer);
        return box;
    }

    private static RichTextLabel Wrapped(string text, float width)
    {
        var label = new RichTextLabel { MaxWidth = width, Margin = new Thickness(0, 2) };
        label.SetMessage(FormattedMessage.FromMarkup($"[color=#aaaaaa]{FormattedMessage.EscapeText(text)}[/color]"));
        return label;
    }

    private void AddHeading(string text)
    {
        _body.AddChild(new Label { Text = text, StyleClasses = { "LabelHeading" }, Margin = new Thickness(0, 10, 0, 2) });
    }
}
