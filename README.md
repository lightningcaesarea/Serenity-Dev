<p align="center">
  <img alt="Space Station 14" width="600" src="Resources/Textures/Logo/logo.png" />
</p>

<div align="center">

# SERENITY
<sub>A Space Station 14 fork built on Starlight</sub>

</div>

> [!WARNING]
> **Serenity is an 18+ project.** It adds adult, NSFW and mature roleplay systems to Space Station 14.
> The repository, its content and any server running it are intended for adults only. If that is not
> what you are looking for, upstream [Starlight](https://github.com/ss14Starlight/space-station-14)
> is the same game without it.

## What Serenity is

Serenity is a private-community fork of [Starlight](https://github.com/ss14Starlight/space-station-14),
itself a fork of [Space Station 14](https://spacestation14.io/). It keeps Starlight's medium-roleplay
foundation and extends it in three directions:

- **Adult roleplay, consent-first.** A per-player kink list and a set of hard consent toggles that the
  server enforces. Nothing adult happens to a character unless its player has opted in, and players can
  see each other's published preferences before anything starts.
- **A persistent economy.** Two currencies (Federal Bills and Sector Credits), a currency exchange,
  balances that survive disconnects and restarts, and an audit ledger behind every transaction.
- **Ships and persistence** (in progress). Player-owned vessels that outlive the round, modelled on
  Frontier's ownership loop and rebuilt from the ground up to keep the codebase MIT.

Fork-specific code lives under `_Serenity` namespaces and directories; Starlight's own work stays under
`_Starlight`. Player-facing text uses Serenity's names (Federation, Sector Station Administration,
Federal Bills, Sector Credits, Station Commander and Station Officer for the HoP and Assistant Manager jobs).
Plasma is called **phoron** in every name, description and locale string; prototype and material ids keep
`plasma` so upstream content still loads, and new display text must not reintroduce the old word.

## Building and running

Standard Space Station 14 workflow — see the
[upstream setup guide](https://docs.spacestation14.com/en/general-development/setup/setting-up-a-development-environment.html):

1. Clone (do not download as zip — the engine is a submodule).
2. `python RUN_THIS.py` to initialise submodules.
3. `dotnet build`, then `dotnet run --project Content.Server` and `dotnet run --project Content.Client`.

On Windows, `Launcher.bat` in the repository root opens a small GUI that builds and launches the server and
one or more clients.

## Contributing

This is a small private-community project rather than an open contribution hub. Issues and pull requests
are welcome but may not be acted on quickly. Anything ported from another fork must be licence-compatible:
Serenity is MIT, so **code from AGPL forks (Frontier and HardLight after July 2024, Floof/Panta-Rhei's own
work, Delta-V) cannot be copied in** — it has to be reimplemented from design.

---

## License

Serenity's own contributions are licensed under the [MIT license](https://opensource.org/license/MIT)
(`LICENSE.TXT`).

Serenity is built on Starlight and attributes the Starlight project as its source:
<https://github.com/ss14Starlight/space-station-14>.

> [!NOTE]
> **Starlight relicensing in progress.** The Starlight Fork License (`LICENSE-Starlight.TXT`) applies to
> Starlight contributions made from **2024-11-04** (commit `84205e38`) through **2026-02-28** (commit
> `01eff0f7`) until explicit relicensing consent is received from the respective authors, after which they
> become MIT. Starlight contributions outside that range, and all Space Wizards Federation code, are MIT
> (`LICENSE.TXT`). Starlight tracks relicensing in
> [ss14Starlight/space-station-14#3499](https://github.com/ss14Starlight/space-station-14/issues/3499).

Non-code assets (sprites, sounds, and other content) are licensed under
[Creative Commons 3.0 BY-SA](https://creativecommons.org/licenses/by-sa/3.0/) unless a folder or file
states otherwise; per-file attributions are recorded in `attributions.yml` files and RSI `meta.json`.
