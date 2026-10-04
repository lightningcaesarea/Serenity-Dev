## Currency exchange
currency-exchange-success = Exchanged {$input} for {$output}.
currency-exchange-too-small = That is too little to exchange.

## Per-character balance, shown on each character in the lobby
character-balance-label = {$balance} Federal Bills
character-balance-label-starting = {$balance} Federal Bills ({$starting} starting funds)
character-balance-unknown = Balance loading...

## Starting funds: spendable by the character they were issued to, never transferable
economy-atm-ui-starting-funds = [color=#88c0ff]{$amount} cr. of that is starting funds.[/color] They withdraw as issued bills that only you can use, and can't be transferred.
economy-atm-transfer-error-starting-funds = Starting funds can't be transferred. You can send up to {$max} cr.
bound-cash-examine = [color=#88c0ff]Issued to {$name}. Nobody else can use these.[/color]
bound-cash-not-yours = These bills aren't registered to you.
stack-bound-bill = issued Federal Bill

## Balance admin commands
cmd-balance-desc = Show the Federal Bill balance of each of a player's characters and their five most recent ledger entries.
cmd-balance-help = Usage: balance <name or user id>
cmd-balance_ledger-desc = Show a player's Federal Bill ledger across all their characters, newest first.
cmd-balance_ledger-help = Usage: balance_ledger <name or user id> [count, default 20, max 500]
cmd-balance_adjust-desc = Add or remove Federal Bills from one of a player's characters, online or offline. Logged with the reason.
cmd-balance_adjust-help = Usage: balance_adjust <name or user id> <character slot> <delta> <reason...>  (slots are listed by "balance")
cmd-balance_set-desc = Set one of a player's characters to an exact amount of Federal Bills, online or offline. Logged with the reason.
cmd-balance_set-help = Usage: balance_set <name or user id> <character slot> <value> <reason...>  (slots are listed by "balance")

cmd-balance-player-not-found = No player found matching "{$player}".
cmd-balance-bad-amount = Amount must be a non-zero number (non-negative for set).
cmd-balance-bad-count = Count must be a whole number between 1 and 500.
cmd-balance-bad-slot = Character slot must be a whole number; "balance <player>" lists them.
cmd-balance-no-character = {$player} has no character in slot {$slot}.
cmd-balance-failed = Could not change that balance. If the character just spawned its balance may still be loading; try again.
cmd-balance-header = {$player} ({$state}) — characters:
cmd-balance-character = slot {$slot}: {$name} — {$balance}{$playing ->
    [yes] {" "}(playing now)
   *[no] {""}
}
cmd-balance-never-spawned = never spawned (will start with {$starting})
cmd-balance-recent = Recent ledger:
cmd-balance-ledger-header = Ledger for {$player} — {$count} most recent:
cmd-balance-ledger-empty = {$player} has no ledger entries.
cmd-balance-adjusted = Adjusted {$player}'s {$name} by {$delta}; balance is now {$balance}.
cmd-balance-set = Set {$player}'s {$name} from {$before} to {$balance}.
