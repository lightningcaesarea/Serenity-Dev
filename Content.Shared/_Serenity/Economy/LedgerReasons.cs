namespace Content.Shared._Serenity.Economy;

/// <summary>
/// The reason strings written to the credit ledger (<c>serenity_resource_transaction.Reason</c>).
/// Every credit movement should use one of these so admins can tell why a balance changed and so the
/// two sides of a transfer can be matched up. Reasons look like <c>kind</c> or <c>kind:detail</c>.
/// </summary>
public static class LedgerReasons
{
    /// <summary>A change made through Starlight's reason-less interface; ideally nothing uses this.</summary>
    public const string Unspecified = "unspecified";

    /// <summary>Credits earned or spent between connecting and the DB load finishing, applied once the load lands.</summary>
    public const string LoadMerge = "load-merge";

    /// <summary>A character's account being opened with the starting balance the first time it spawns.</summary>
    public const string StartingBalance = "starting-balance";

    public const string AtmDeposit = "atm-deposit";
    public const string AtmWithdraw = "atm-withdraw";
    public const string Donate = "donate";

    public static string Salary(string role) => $"salary:{role}";

    /// <summary>An admin command. <paramref name="note"/> is the free-text reason the admin gave.</summary>
    public static string Admin(string actor, string note) => $"admin:{actor}: {note}";

    /// <summary>The sending side of an ATM transfer; <paramref name="recipient"/> is the receiving player's user id.</summary>
    public static string TransferOut(Guid recipient) => $"atm-transfer-out:{recipient}";

    /// <summary>The receiving side of an ATM transfer; <paramref name="sender"/> is the sending player's user id.</summary>
    public static string TransferIn(Guid sender) => $"atm-transfer-in:{sender}";

    /// <summary>The fee held when a proposal is filed at the secure command terminal.</summary>
    public static string TerminalFee(string requestId) => $"terminal-fee:{requestId}";

    public static string TerminalRefund(string requestId) => $"terminal-refund:{requestId}";
}
