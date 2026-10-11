## Tokamak fusion engine

research-technology-fusion-power = Fusion Power
guide-entry-tokamak = Tokamak
guide-entry-tokamak-setup = Building and starting a tokamak
guide-entry-tokamak-fuels = Fuels and reactions
guide-entry-tokamak-safety = Hazards and shutdown

tokamak-injector-rod-slot = Fuel rod

# Reactants
tokamak-reactant-hydrogen = Hydrogen
tokamak-reactant-deuterium = Deuterium
tokamak-reactant-tritium = Tritium
tokamak-reactant-helium3 = Helium-3
tokamak-reactant-helium4 = Helium ash
tokamak-reactant-lithium = Lithium
tokamak-reactant-boron = Boron
tokamak-reactant-iron = Iron
tokamak-reactant-silver = Silver
tokamak-reactant-gold = Gold

# Core
tokamak-core-scram-lockout = The field magnets are locked out after a SCRAM. { $seconds } seconds until they can be restarted.
tokamak-core-no-power = The core has no power to hold a field with.
tokamak-core-warning-1 = {CAPITALIZE(THE($core))} field is wobbling. Instability at { $instability }%.
tokamak-core-warning-2 = {CAPITALIZE(THE($core))} field is failing! Instability at { $instability }%. Consider a SCRAM.
tokamak-core-warning-3 = {CAPITALIZE(THE($core))} field is about to rupture! Instability at { $instability }%. Evacuate!
tokamak-core-rupture = {CAPITALIZE(THE($core))} field has ruptured!

# Devices
tokamak-device-no-power = The device has no power.
tokamak-gyrotron-examine = The gyrotron is [color=yellow]{ $state }[/color], firing at rate { $rate } with { $energy } MeV pulses.
tokamak-injector-examine = The fuel injector is [color=yellow]{ $state }[/color], feeding at { $rate } units per second.
tokamak-harvester-examine = The harvester is [color=yellow]{ $state }[/color].
tokamak-rod-examine-entry = It holds { $amount } units of { $reactant }.
tokamak-rod-examine-empty = It is empty.

# Console
tokamak-ui-title = Tokamak Console
tokamak-ui-no-core = No tokamak core found in range.
tokamak-ui-field-state = Field
tokamak-ui-temperature = Plasma temperature
tokamak-ui-output = Power output
tokamak-ui-draw = Power draw
tokamak-ui-radiation = Radiation
tokamak-ui-plasma = Plasma
tokamak-ui-instability = Instability
tokamak-ui-reactants = Reactants
tokamak-ui-reactants-empty = The field is empty.
tokamak-ui-reactant-entry = { $reactant }: { $amount }
tokamak-ui-field-strength = Field strength
tokamak-ui-field-on = Start field
tokamak-ui-field-off = Stop field
tokamak-ui-scram = SCRAM
tokamak-ui-scram-tooltip = Dump the field and settle the plasma at once, clearing all instability. Releases some radiation and locks the field out for five minutes.
tokamak-ui-devices = Devices
tokamak-ui-no-devices = No devices in range.
tokamak-ui-state-on = Active
tokamak-ui-state-off = Off
tokamak-ui-state-unpowered = No power
tokamak-ui-kelvin = { $value } K
tokamak-ui-number = { $value }
tokamak-ui-percent = { $value }%
tokamak-ui-watts = { $value } W
tokamak-ui-kilowatts = { $value } kW
tokamak-ui-megawatts = { $value } MW
tokamak-ui-plasma-amount = { $amount } / { $max }
tokamak-ui-kind-gyrotron = Gyrotron
tokamak-ui-kind-injector = Injector
tokamak-ui-kind-harvester = Harvester
tokamak-ui-device-name = { $kind }: { $name }
tokamak-ui-aligned = Aligned
tokamak-ui-not-aligned = Not aligned
tokamak-ui-device-on = Turn on
tokamak-ui-device-off = Turn off
tokamak-ui-gyrotron-rate = Rate: { $value }
tokamak-ui-gyrotron-energy = Energy: { $value } MeV
tokamak-ui-injector-rate = Rate: { $value } (rod { $fill }% full)
