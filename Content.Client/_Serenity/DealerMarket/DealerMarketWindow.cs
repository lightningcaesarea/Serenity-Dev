using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Serenity.DealerMarket;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Serenity.DealerMarket;

/// <summary>
/// The dealer market terminal. Tabs for the market, contracts, rate board, turn-in and history; on the market tab a
/// dealer list on the left, the chosen dealer's goods in the middle and the player's contract and cargo lift on the right.
/// </summary>
public sealed partial class DealerMarketWindow : FancyWindow
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IEntityManager _entMan = default!;

    public event Action<int>? OnFulfil;
    public event Action<int>? OnDecline;
    public event Action<int>? OnRequestCrate;
    public event Action<ProtoId<DealerPrototype>, int>? OnBuy;
    public event Action<ProtoId<DealerPrototype>>? OnSell;

    private const int MaxHistory = 30;

    private readonly Label _balance = new() { HorizontalAlignment = HAlignment.Right, HorizontalExpand = true };
    private readonly Label _status = new() { StyleClasses = { "LabelSubText" } };
    private readonly TabContainer _tabs = new() { VerticalExpand = true };
    private readonly BoxContainer _dealerList = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly BoxContainer _dealerBody = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly BoxContainer _side = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, MinWidth = 230 };
    private readonly BoxContainer _contractsTab = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly BoxContainer _rateTab = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly BoxContainer _turnInTab = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly BoxContainer _historyTab = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };

    private readonly ScrollContainer _contractsScroll;
    private ProtoId<DealerPrototype>? _selected;
    private DealerMarketStateMessage? _state;
    private int? _lastBalance;
    private string? _pendingAction;
    private readonly List<string> _history = new();

    public DealerMarketWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("dealer-market-title");
        MinSize = new Vector2(760, 520);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8) };
        var top = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        top.AddChild(new Label { Text = Loc.GetString("dealer-market-heading"), StyleClasses = { "LabelHeading" } });
        top.AddChild(_balance);
        root.AddChild(top);
        root.AddChild(_status);

        var market = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        var left = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, MinWidth = 170 };
        left.AddChild(new Label { Text = Loc.GetString("dealer-market-on-station"), StyleClasses = { "LabelSubText" } });
        left.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _dealerList } });
        market.AddChild(left);
        market.AddChild(new ScrollContainer
        {
            VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false, Children = { _dealerBody },
        });
        market.AddChild(_side);

        AddTab(market, "dealer-market-tab-market");
        _contractsScroll = Scroll(_contractsTab);
        AddTab(_contractsScroll, "dealer-market-tab-contracts");
        AddTab(Scroll(_rateTab), "dealer-market-tab-rates");
        AddTab(Scroll(_turnInTab), "dealer-market-tab-turn-in");
        AddTab(Scroll(_historyTab), "dealer-market-tab-history");
        root.AddChild(_tabs);
        ContentsContainer.AddChild(root);
    }

    private void AddTab(Control control, string title)
    {
        _tabs.AddChild(control);
        TabContainer.SetTabTitle(control, Loc.GetString(title));
    }

    private static ScrollContainer Scroll(Control child)
    {
        return new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { child } };
    }

    public void Update(DealerMarketStateMessage state)
    {
        _state = state;

        var dealers = _proto.EnumeratePrototypes<DealerPrototype>().OrderBy(d => d.Name).ToList();
        if (_selected == null || !dealers.Any(d => d.ID == _selected))
            _selected = dealers.FirstOrDefault()?.ID;

        // Whatever the player last clicked gets a history line once the balance actually moves.
        if (_lastBalance is { } before && before != state.Balance && _pendingAction != null)
        {
            var delta = state.Balance - before;
            _history.Insert(0, $"{_pendingAction}: {(delta > 0 ? "+" : "")}{delta}");
            if (_history.Count > MaxHistory)
                _history.RemoveAt(_history.Count - 1);
            _pendingAction = null;
        }
        _lastBalance = state.Balance;

        _balance.Text = Loc.GetString("dealer-market-balance", ("balance", state.Balance));
        _status.Text = Loc.GetString("dealer-market-status",
            ("elevator", Loc.GetString(state.HasElevator ? "dealer-market-linked" : "dealer-market-not-linked")),
            ("intake", Loc.GetString(state.HasIntake ? "dealer-market-linked" : "dealer-market-not-linked")),
            ("outlet", Loc.GetString(state.HasOutlet ? "dealer-market-linked" : "dealer-market-not-linked")),
            ("incoming", state.Incoming));
        TabContainer.SetTabTitle(_contractsScroll,
            Loc.GetString("dealer-market-tab-contracts-count", ("count", state.Contracts.Count)));

        PopulateDealerList(dealers, state);
        _dealerBody.RemoveAllChildren();
        if (_selected is { } current)
            PopulateDealer(_proto.Index(current), state);
        PopulateSide(state);

        _contractsTab.RemoveAllChildren();
        if (state.Contracts.Count == 0)
            _contractsTab.AddChild(Quiet(Loc.GetString("dealer-market-no-contracts")));
        foreach (var contract in state.Contracts)
            _contractsTab.AddChild(ContractBox(contract));

        PopulateRates(dealers, state);
        PopulateTurnIn(state);

        _historyTab.RemoveAllChildren();
        if (_history.Count == 0)
            _historyTab.AddChild(Quiet(Loc.GetString("dealer-market-history-empty")));
        foreach (var line in _history)
            _historyTab.AddChild(new Label { Text = line });
    }

    private void PopulateDealerList(List<DealerPrototype> dealers, DealerMarketStateMessage state)
    {
        _dealerList.RemoveAllChildren();
        foreach (var dealer in dealers)
        {
            var id = dealer.ID;
            var button = new Button { ToggleMode = true, Pressed = id == _selected, Margin = new Thickness(0, 2) };
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            row.AddChild(new TextureRect
            {
                Texture = _entMan.System<SpriteSystem>().Frame0(dealer.Portrait),
                TextureScale = new Vector2(dealer.PortraitScale > 1 ? 1 : 1.5f),
                Stretch = TextureRect.StretchMode.Keep,
                MinSize = new Vector2(48, 48),
                MaxSize = new Vector2(48, 48),
                Margin = new Thickness(0, 0, 6, 0),
            });
            var text = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalAlignment = VAlignment.Center };
            var waiting = state.Contracts.Count(c => c.Dealer == id);
            text.AddChild(new Label { Text = waiting > 0 ? $"{dealer.Name} ({waiting})" : dealer.Name });
            text.AddChild(new Label { Text = dealer.Trade, StyleClasses = { "LabelSubText" } });
            row.AddChild(text);
            button.AddChild(row);
            button.OnPressed += _ =>
            {
                _selected = id;
                Update(_state!);
            };
            _dealerList.AddChild(button);
        }
    }

    private void PopulateDealer(DealerPrototype dealer, DealerMarketStateMessage state)
    {
        var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        header.AddChild(new TextureRect
        {
            Texture = _entMan.System<SpriteSystem>().Frame0(dealer.Portrait),
            TextureScale = new Vector2(dealer.PortraitScale, dealer.PortraitScale),
            Stretch = TextureRect.StretchMode.Keep,
            MinSize = new Vector2(120, 100),
        });
        var intro = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(10, 0) };
        intro.AddChild(Wrapped($"\"{dealer.Greeting}\"", 300));
        intro.AddChild(new Label { Text = $"- {dealer.Name}", StyleClasses = { "LabelSubText" } });
        header.AddChild(intro);
        _dealerBody.AddChild(header);

        if (dealer.Buys != null)
        {
            AddHeading(Loc.GetString("dealer-market-they-buy"));
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
            sell.OnPressed += _ =>
            {
                _pendingAction = Loc.GetString("dealer-market-history-sold", ("dealer", dealer.Name));
                OnSell?.Invoke(dealer.ID);
            };
            row.AddChild(sell);
            _dealerBody.AddChild(row);

            if (state.Pad.Count == 0)
                _dealerBody.AddChild(Quiet(Loc.GetString("dealer-market-pad-empty")));
            foreach (var item in state.Pad)
                _dealerBody.AddChild(new Label { Text = $"{item.Name} x{item.Count}" });
        }

        if (dealer.Stock.Count > 0)
        {
            AddHeading(Loc.GetString("dealer-market-they-sell"));
            for (var i = 0; i < dealer.Stock.Count; i++)
            {
                var index = i;
                var offer = dealer.Stock[i];
                var name = _proto.Index(offer.Item).Name;
                var label = offer.Amount > 1 ? $"{name} ({offer.Amount})" : name;
                var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
                row.AddChild(new Label { Text = label, HorizontalExpand = true });
                var buy = new Button
                {
                    Text = Loc.GetString("dealer-market-order", ("price", offer.Price)),
                    Disabled = state.Balance < offer.Price || !state.HasOutlet,
                };
                buy.OnPressed += _ =>
                {
                    _pendingAction = Loc.GetString("dealer-market-history-ordered", ("item", label), ("dealer", dealer.Name));
                    OnBuy?.Invoke(dealer.ID, index);
                };
                row.AddChild(buy);
                _dealerBody.AddChild(row);
            }
        }
    }

    /// <summary>The right-hand panel: the player's most urgent contract with this dealer (or any), and the lift.</summary>
    private void PopulateSide(DealerMarketStateMessage state)
    {
        _side.RemoveAllChildren();
        _side.AddChild(new Label { Text = Loc.GetString("dealer-market-your-contract"), StyleClasses = { "LabelSubText" } });

        var contract = state.Contracts.FirstOrDefault(c => c.Dealer == _selected && c.HasCrate);
        contract ??= state.Contracts.FirstOrDefault(c => c.HasCrate);
        contract ??= state.Contracts.FirstOrDefault(c => c.Dealer == _selected);
        contract ??= state.Contracts.FirstOrDefault();
        if (contract == null)
        {
            _side.AddChild(Quiet(Loc.GetString("dealer-market-no-contracts")));
            return;
        }

        _side.AddChild(CompactContract(contract, true));
    }

    private Control CompactContract(DealerContractInfo contract, bool withLift)
    {
        var box = new PanelContainer
        {
            PanelOverride = new Robust.Client.Graphics.StyleBoxFlat(Color.FromHex("#25252a")),
            Margin = new Thickness(2, 3),
        };
        var inner = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(6) };
        box.AddChild(inner);

        var dealer = _proto.Index(contract.Dealer);
        inner.AddChild(new Label
        {
            Text = contract.RoleContract ? Loc.GetString("dealer-market-badge-role") : dealer.Name,
            StyleClasses = { "LabelSubText" },
        });
        inner.AddChild(new Label { Text = contract.Title, StyleClasses = { "LabelHeading" } });
        foreach (var want in contract.Wants)
        {
            inner.AddChild(new Label
            {
                Text = Loc.GetString("dealer-market-want", ("name", want.Name), ("inCrate", want.InCrate), ("wanted", want.Wanted)),
            });
            inner.AddChild(new ProgressBar
            {
                MinValue = 0,
                MaxValue = Math.Max(1, want.Wanted),
                Value = Math.Min(want.InCrate, want.Wanted),
                MinHeight = 8,
            });
        }
        inner.AddChild(new Label
        {
            Text = Loc.GetString("dealer-market-contract-footer",
                ("payout", contract.Payout), ("minutes", (contract.SecondsLeft + 59) / 60)),
            StyleClasses = { "LabelSubText" },
        });

        if (withLift)
            inner.AddChild(LiftBox(contract));
        return box;
    }

    private Control LiftBox(DealerContractInfo contract)
    {
        var lift = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(0, 8, 0, 0) };
        lift.AddChild(new Label
        {
            Text = Loc.GetString(contract.HasCrate ? "dealer-market-lift-ready" : "dealer-market-lift-idle"),
            StyleClasses = { "LabelHeading" },
        });
        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        var raise = new Button
        {
            Text = Loc.GetString("dealer-market-raise-lift"),
            Disabled = contract.HasCrate || _state?.HasElevator != true,
            HorizontalExpand = true,
        };
        raise.OnPressed += _ => OnRequestCrate?.Invoke(contract.Id);
        var send = new Button
        {
            Text = Loc.GetString("dealer-market-send-and-submit"),
            Disabled = !contract.HasCrate || !contract.Ready,
            HorizontalExpand = true,
        };
        send.OnPressed += _ =>
        {
            _pendingAction = Loc.GetString("dealer-market-history-contract", ("title", contract.Title));
            OnFulfil?.Invoke(contract.Id);
        };
        buttons.AddChild(raise);
        buttons.AddChild(send);
        lift.AddChild(buttons);
        return lift;
    }

    private void PopulateRates(List<DealerPrototype> dealers, DealerMarketStateMessage state)
    {
        _rateTab.RemoveAllChildren();
        foreach (var dealer in dealers)
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 2) };
            row.AddChild(new Label { Text = dealer.Name, MinWidth = 180 });
            row.AddChild(new Label { Text = dealer.Trade, HorizontalExpand = true, StyleClasses = { "LabelSubText" } });
            if (dealer.Buys == null)
            {
                row.AddChild(Quiet(Loc.GetString("dealer-market-rate-no-buy")));
            }
            else
            {
                state.Quotes.TryGetValue(dealer.ID, out var quote);
                row.AddChild(new Label
                {
                    Text = Loc.GetString("dealer-market-rate-line", ("rate", (int) (dealer.BuyRate * 100)), ("payout", quote)),
                });
            }
            _rateTab.AddChild(row);
        }
    }

    private void PopulateTurnIn(DealerMarketStateMessage state)
    {
        _turnInTab.RemoveAllChildren();
        if (state.Contracts.Count == 0)
            _turnInTab.AddChild(Quiet(Loc.GetString("dealer-market-no-contracts")));
        foreach (var contract in state.Contracts)
            _turnInTab.AddChild(CompactContract(contract, true));
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
                Text = Loc.GetString("dealer-market-want", ("name", want.Name), ("inCrate", want.InCrate), ("wanted", want.Wanted)),
                FontColorOverride = want.InCrate >= want.Wanted ? Color.LightGreen : null,
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
        if (contract.HasCrate)
        {
            var fulfil = new Button { Text = Loc.GetString("dealer-market-fulfil"), Disabled = !contract.Ready };
            fulfil.OnPressed += _ => OnFulfil?.Invoke(contract.Id);
            footer.AddChild(fulfil);
        }
        else
        {
            var request = new Button { Text = Loc.GetString("dealer-market-request-crate") };
            request.OnPressed += _ => OnRequestCrate?.Invoke(contract.Id);
            footer.AddChild(request);
        }
        inner.AddChild(footer);
        return box;
    }

    private static RichTextLabel Wrapped(string text, float width)
    {
        var label = new RichTextLabel { MaxWidth = width, Margin = new Thickness(0, 2) };
        label.SetMessage(FormattedMessage.FromMarkupOrThrow($"[color=#aaaaaa]{FormattedMessage.EscapeText(text)}[/color]"));
        return label;
    }

    private static Label Quiet(string text)
    {
        return new Label { Text = text, StyleClasses = { "LabelSubText" } };
    }

    private void AddHeading(string text)
    {
        _dealerBody.AddChild(new Label { Text = text, StyleClasses = { "LabelHeading" }, Margin = new Thickness(0, 10, 0, 2) });
    }
}
