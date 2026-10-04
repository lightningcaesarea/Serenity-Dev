using Content.Shared._Starlight.Economy.Atm;
using JetBrains.Annotations;
using Robust.Shared.Utility;

namespace Content.Client._Starlight.Economy.Atm;

[UsedImplicitly]
public sealed class ATMBui : BoundUserInterface
{
    [ViewVariables]
    private ATMWindow? _window;
    private int _amount;
    private int _transferAmount;
    private string _transferRecipient = string.Empty;
    private int _currentBalance;
    private int _transferable; // Serenity: balance minus starting funds
    public ATMBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {

    }
    protected override void Open()
    {
        base.Open();
        TryInitWindow();
        View(ViewType.Withdraw);
        UpdateState(State);
        _window?.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState? state)
    {
        if (state is ATMBuiState s)
            Update(s);
    }

    private void Update(ATMBuiState state)
        => RefreshUI(state);

    private void TryInitWindow()
    {
        if (_window != null) return;
        _window = new ATMWindow();
        _window.OnClose += Close;
        _window.Title = "Automated Teller Machine";

        _window.WithdrawTabButton.OnPressed += _ =>
        {
            View(ViewType.Withdraw);
            _transferRecipient = string.Empty;
            _transferAmount = 0;
            _window.TransferTargetInput.Text = string.Empty;
            _window.TransferAmountInput.Text = string.Empty;
            _window.TransferButton.Disabled = true;
        };

        _window.TransferTabButton.OnPressed += _ =>
        {
            View(ViewType.Transfer);
            _window.TransferButton.Disabled = string.IsNullOrWhiteSpace(_transferRecipient)
                || _transferAmount <= 0 || _transferAmount > _transferable;
        };

        _window.WithdrawInput.OnTextChanged += _ =>
            _window.WithdrawButton.Disabled = !int.TryParse(_window.WithdrawInput.Text, out _amount)
                || _amount <= 0 || _amount > _currentBalance;

        _window.WithdrawButton.OnPressed += _ =>
        {
            SendMessage(new ATMWithdrawBuiMsg { Amount = _amount });
            _window.WithdrawButton.Disabled = true;
        };

        _window.TransferAmountInput.OnTextChanged += _ =>
        {
            if (!int.TryParse(_window.TransferAmountInput.Text, out _transferAmount))
                _transferAmount = 0;
            _window.TransferButton.Disabled = string.IsNullOrWhiteSpace(_transferRecipient)
                || _transferAmount <= 0 || _transferAmount > _transferable;
        };

        _window.TransferTargetInput.OnTextChanged += _ =>
            _window.TransferButton.Disabled = string.IsNullOrWhiteSpace(_transferRecipient = _window.TransferTargetInput.Text ?? string.Empty)
                || _transferAmount <= 0 || _transferAmount > _transferable;

        _window.TransferButton.OnPressed += _ =>
        {
            if (!string.IsNullOrWhiteSpace(_transferRecipient) && _transferAmount > 0)
            {
                SendMessage(new ATMTransferBuiMsg
                {
                    Recipient = _transferRecipient.Trim(),
                    Amount = _transferAmount
                });
            }
            _transferRecipient = string.Empty;
            _transferAmount = 0;
            _window.TransferTargetInput.Text = string.Empty;
            _window.TransferAmountInput.Text = string.Empty;
            _window.TransferButton.Disabled = true;
        };
    }

    private void RefreshUI(ATMBuiState state)
    {
        if (_window == null)
            return;

        _currentBalance = state.Balance;
        _transferable = Math.Max(0, state.Balance - state.StartingFunds); // Serenity
        _window.BalanceLabel.Children.Clear();

        var balanceMsg = new FormattedMessage();
        balanceMsg.AddMarkupOrThrow(Loc.GetString("economy-atm-ui-balance", ("balance", state.Balance)));
        // Serenity: starting funds withdraw as bills bound to this character and can't be transferred
        if (state.StartingFunds > 0)
        {
            balanceMsg.PushNewline();
            balanceMsg.AddMarkupOrThrow(Loc.GetString("economy-atm-ui-starting-funds", ("amount", state.StartingFunds)));
        }
        _window.BalanceLabel.SetMessage(balanceMsg);

        _window.TransferHelpLabel.Children.Clear();

        var transferHelp = new FormattedMessage();
        transferHelp.AddText(Loc.GetString("economy-atm-ui-transfer-help"));
        _window.TransferHelpLabel.SetMessage(transferHelp);

        if (!string.IsNullOrWhiteSpace(state.Message))
        {
            var msg = new FormattedMessage();
            var color = state.IsError ? "red" : "green";
            msg.AddMarkupOrThrow($"[color={color}]");
            msg.AddText(state.Message!);
            msg.AddMarkupOrThrow("[/color]");
            _window.TransferHelpLabel.SetMessage(msg);
        }

        _window.WithdrawButton.Disabled = !int.TryParse(_window.WithdrawInput.Text, out _amount)
            || _amount <= 0 || _amount > _currentBalance;

        _window.TransferButton.Disabled = string.IsNullOrWhiteSpace(_transferRecipient)
            || _transferAmount <= 0 || _transferAmount > _transferable;
    }

    private void View(ViewType type)
    {
        if (_window == null)
            return;

        _window.WithdrawTabButton.Parent!.Margin = new Thickness(0, 0, 0, 10);

        _window.WithdrawTabButton.Disabled = type == ViewType.Withdraw;
        _window.TransferTabButton.Disabled = type == ViewType.Transfer;
        _window.WithdrawTab.Visible = type == ViewType.Withdraw;
        _window.TransferTab.Visible = type == ViewType.Transfer;
    }

    private enum ViewType
    {
        Withdraw,
        Transfer
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        if (_window == null)
            return;

        _window.OnClose -= Close;
        _window.Orphan();
        _window = null;
    }
}
