using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Shared._Serenity.Economy;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._Serenity.Economy.Commands;

/// <summary>
/// Shared plumbing for the balance commands. Money is per-character, so every balance is a character's: the
/// manager routes a change through the in-round cache when that character is being played, and to the DB otherwise.
/// </summary>
public abstract partial class BaseBalanceCommand : LocalizedCommands
{
    [Dependency] protected IPlayerLocator Locator = default!;
    [Dependency] protected IPlayerManager Players = default!;
    [Dependency] protected IServerDbManager Db = default!;
    [Dependency] protected ICharacterBalanceManager Balances = default!;
    [Dependency] protected IAdminLogManager AdminLog = default!;

    protected async Task<LocatedPlayerData?> Locate(IConsoleShell shell, string nameOrId)
    {
        var located = await Locator.LookupIdByNameOrIdAsync(nameOrId);
        if (located == null)
            shell.WriteError(Loc.GetString("cmd-balance-player-not-found", ("player", nameOrId)));
        return located;
    }

    /// <summary>The player's character in <paramref name="slotArg"/>, or null (with an error printed).</summary>
    protected async Task<CharacterBalanceSummary?> LocateCharacter(IConsoleShell shell, LocatedPlayerData located, string slotArg)
    {
        if (!int.TryParse(slotArg, out var slot))
        {
            shell.WriteError(Loc.GetString("cmd-balance-bad-slot"));
            return null;
        }

        var character = (await Balances.GetBalancesAsync(located.UserId)).FirstOrDefault(c => c.Slot == slot);
        if (character == null)
            shell.WriteError(Loc.GetString("cmd-balance-no-character", ("player", located.Username), ("slot", slot)));
        return character;
    }

    protected static string Actor(IConsoleShell shell)
        => shell.Player?.Name ?? "SERVER";

    protected static string Fmt(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);

    protected static Dictionary<int, string> Names(IEnumerable<CharacterBalanceSummary> characters)
        => characters.ToDictionary(c => c.ProfileId, c => c.Name);

    /// <summary>One ledger row, naming the character it belongs to if it still exists.</summary>
    protected static string FormatLedgerRow(PlayerResourceTransaction row, IReadOnlyDictionary<int, string> names)
    {
        var who = row.ProfileId is not { } id
            ? "account"
            : names.TryGetValue(id, out var name) ? name : $"deleted #{id}";

        return $"  {row.CreatedAt:yyyy-MM-dd HH:mm:ss}  {(row.Delta >= 0 ? "+" : "")}{Fmt(row.Delta),10}  => {Fmt(row.BalanceAfter),10}  [{who}] {row.Reason ?? LedgerReasons.Unspecified}";
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class BalanceCommand : BaseBalanceCommand
{
    public override string Command => "balance";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteLine(Help);
            return;
        }

        if (await Locate(shell, args[0]) is not { } located)
            return;

        var online = Players.TryGetSessionById(located.UserId, out _);
        var playing = Balances.GetActiveSlot(located.UserId);
        var characters = await Balances.GetBalancesAsync(located.UserId);
        var ledger = await Db.GetPlayerResourceTransactions(located.UserId.UserId, 5);

        var sb = new StringBuilder();
        sb.AppendLine(Loc.GetString("cmd-balance-header",
            ("player", located.Username),
            ("state", online ? "online" : "offline")));

        foreach (var character in characters)
        {
            var balance = character.Balance is { } b
                ? Fmt(b)
                : Loc.GetString("cmd-balance-never-spawned", ("starting", Fmt(Balances.StartingBalance)));

            sb.Append("  ").AppendLine(Loc.GetString("cmd-balance-character",
                ("slot", character.Slot),
                ("name", character.Name),
                ("balance", balance),
                ("playing", character.Slot == playing ? "yes" : "no")));
        }

        sb.AppendLine(Loc.GetString("cmd-balance-recent"));
        var names = Names(characters);
        foreach (var row in ledger)
            sb.AppendLine(FormatLedgerRow(row, names));

        shell.WriteLine(sb.ToString().TrimEnd());
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class BalanceLedgerCommand : BaseBalanceCommand
{
    public override string Command => "balance_ledger";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteLine(Help);
            return;
        }

        var count = 20;
        if (args.Length == 2 && (!int.TryParse(args[1], out count) || count < 1 || count > 500))
        {
            shell.WriteError(Loc.GetString("cmd-balance-bad-count"));
            return;
        }

        if (await Locate(shell, args[0]) is not { } located)
            return;

        var ledger = await Db.GetPlayerResourceTransactions(located.UserId.UserId, count);
        if (ledger.Count == 0)
        {
            shell.WriteLine(Loc.GetString("cmd-balance-ledger-empty", ("player", located.Username)));
            return;
        }

        var names = Names(await Balances.GetBalancesAsync(located.UserId));
        var sb = new StringBuilder();
        sb.AppendLine(Loc.GetString("cmd-balance-ledger-header", ("player", located.Username), ("count", ledger.Count)));
        foreach (var row in ledger)
            sb.AppendLine(FormatLedgerRow(row, names));
        shell.WriteLine(sb.ToString().TrimEnd());
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class BalanceAdjustCommand : BaseBalanceCommand
{
    public override string Command => "balance_adjust";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 4)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var delta) || delta == 0)
        {
            shell.WriteError(Loc.GetString("cmd-balance-bad-amount"));
            return;
        }

        if (await Locate(shell, args[0]) is not { } located
            || await LocateCharacter(shell, located, args[1]) is not { } character)
            return;

        var note = string.Join(' ', args.Skip(3));
        var after = await Balances.AdjustCharacterAsync(located.UserId, character.Slot, delta, LedgerReasons.Admin(Actor(shell), note));
        if (after is not { } balance)
        {
            shell.WriteError(Loc.GetString("cmd-balance-failed"));
            return;
        }

        AdminLog.Add(LogType.Economy, LogImpact.High,
            $"{Actor(shell)} adjusted {located.Username}'s character {character.Name} by {Fmt(delta)} (now {Fmt(balance)}). Reason: {note}");

        shell.WriteLine(Loc.GetString("cmd-balance-adjusted",
            ("player", located.Username), ("name", character.Name), ("delta", Fmt(delta)), ("balance", Fmt(balance))));
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class BalanceSetCommand : BaseBalanceCommand
{
    public override string Command => "balance_set";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 4)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            shell.WriteError(Loc.GetString("cmd-balance-bad-amount"));
            return;
        }

        if (await Locate(shell, args[0]) is not { } located
            || await LocateCharacter(shell, located, args[1]) is not { } character)
            return;

        var before = character.Balance ?? Balances.StartingBalance;
        var note = string.Join(' ', args.Skip(3));
        var after = await Balances.SetCharacterAsync(located.UserId, character.Slot, value, LedgerReasons.Admin(Actor(shell), note));
        if (after is not { } balance)
        {
            shell.WriteError(Loc.GetString("cmd-balance-failed"));
            return;
        }

        AdminLog.Add(LogType.Economy, LogImpact.High,
            $"{Actor(shell)} set {located.Username}'s character {character.Name} to {Fmt(balance)} (was {Fmt(before)}). Reason: {note}");

        shell.WriteLine(Loc.GetString("cmd-balance-set",
            ("player", located.Username), ("name", character.Name), ("before", Fmt(before)), ("balance", Fmt(balance))));
    }
}
