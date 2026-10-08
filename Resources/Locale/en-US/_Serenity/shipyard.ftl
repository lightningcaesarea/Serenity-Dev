## Shipyard console UI
shipyard-console-menu-title = Shipyard
shipyard-console-balance-label = Inserted bills:{" "}
shipyard-console-appraisal-label = Ship value:{" "}
shipyard-console-deed-label = Registered ship:
shipyard-console-deed-none = None
shipyard-console-purchase = Purchase
shipyard-console-sell = Sell ship
shipyard-console-category-small = Small
shipyard-console-category-medium = Medium
shipyard-console-category-large = Large
shipyard-console-category-humongous = Humongous
shipyard-console-save = Save ship
shipyard-console-saved-ships-label = Saved ships:
shipyard-console-saved-ships-none = No saved ships for this character.
shipyard-console-load = Load ({$fee})

## Item slot names
shipyard-console-bills-slot = Federal Bills

## Popups
shipyard-console-no-idcard = No ID card inserted.
shipyard-console-already-deeded = That ID card already holds a ship deed.
shipyard-console-invalid-vessel = That vessel is not for sale here.
shipyard-console-invalid-price = That vessel has no price.
shipyard-console-invalid-station = This console is not attached to a station.
shipyard-console-no-bills = No Federal Bills inserted into the console.
shipyard-console-no-deed = No ship deed on this card.
shipyard-console-sale-reqs = The ship must be docked to the station with nobody aboard.
shipyard-console-saves-disabled = Ship saving is disabled.
shipyard-console-save-not-owner = Only the ship's registered owner can save it.
shipyard-console-save-full = This character already has {$max} saved ships. Load one first.
shipyard-console-save-reqs = The ship must be docked to the station with nobody aboard.
shipyard-console-load-missing = That saved ship no longer exists.
shipyard-console-load-cooldown = You loaded a ship recently. Try again in {$seconds} seconds.
shipyard-console-sale-saved = A ship brought back from a save can't be sold.
shipyard-console-purchase-failed = Failed to prepare the ship. Contact Sector Station Administration.

## Radio announcements
shipyard-console-docking = {$vessel}, registered to {$owner}, is en route to the station. ETA {$delay} seconds.
shipyard-console-saved = {$vessel}, registered to {$owner}, has been moved to long-term storage.
shipyard-console-leaving = {$vessel}, registered to {$owner}, has been sold by {$player}.
shipyard-console-docking-secret = Unregistered vessel detected entering the sector.
shipyard-console-leaving-secret = Unregistered vessel detected leaving the sector.

## purchaseshuttle command
cmd-purchaseshuttle-desc = Spawns and docks a shuttle from a grid file to a station.
cmd-purchaseshuttle-help = purchaseshuttle <station uid> <grid file path> [delay seconds]
cmd-purchaseshuttle-no-entity = No entity with UID { $uid } exists.
cmd-purchaseshuttle-invalid-delay = { $value } is not a valid delay.
cmd-purchaseshuttle-failed = Failed to purchase the shuttle. Check the server log.
cmd-purchaseshuttle-success = Purchased shuttle '{ $path }' for station { $station }.
