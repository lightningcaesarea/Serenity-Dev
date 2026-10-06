#!/usr/bin/env python3
"""
Regenerates Serenity's adult-anatomy marking content from a curated subset of upstream
genital sprites (CC-BY-SA, originally S.P.L.U.R.T-tg / Anzuneth / DrSmugleaf).

Only the sprite ART is taken from the source; the prototypes, categories and systems are Serenity's own.
Each output RSI keeps the source RSI's license and copyright string verbatim plus a note that it is a subset.

    python Tools/_Serenity/gen_genital_markings.py <path to the source Genitals texture folder>

Outputs (all committed, so this only needs re-running to change the curated set):
    Resources/Textures/_Serenity/Genitals/*.rsi
    Resources/Prototypes/_Serenity/Entities/Mobs/Customization/Markings/genitals.yml
    Resources/Locale/en-US/_Serenity/markings/genitals.ftl
"""
import json
import os
import shutil
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEX_OUT = os.path.join(REPO, "Resources", "Textures", "_Serenity", "Genitals")
YML_OUT = os.path.join(REPO, "Resources", "Prototypes", "_Serenity", "Entities", "Mobs", "Customization", "Markings", "genitals.yml")
FTL_OUT = os.path.join(REPO, "Resources", "Locale", "en-US", "_Serenity", "markings", "genitals.ftl")

# ---- curated set -------------------------------------------------------------------------------------------------
PENIS_SHAPES = [  # (id fragment, display name, state pattern)
    ("Human", "Human", "m_penis_human_{n}_{e}_front"),
    ("Knotted", "Knotted", "m_penis_knotted_{n}_{e}_front_primary"),
    ("Flared", "Flared", "m_penis_flared_{n}_{e}_front_primary"),
    ("Tapered", "Tapered", "m_penis_tapered_{n}_{e}_front_primary"),
    ("Hemi", "Hemipenes", "m_penis_hemi_{n}_{e}_front_primary"),
    ("Nondescript", "Nondescript", "m_penis_nondescript_{n}_{e}_front_primary"),
    ("Barbknot", "Barbed and knotted", "m_penis_barbknot_{n}_{e}_front_primary"),
    ("Hemiknot", "Knotted hemipenes", "m_penis_hemiknot_{n}_{e}_front_primary"),
    ("Tentacle", "Tentacle", "m_penis_tentacle_{n}_{e}_front_primary"),
]
PENIS_SIZES = {2: "Small", 4: "Average", 6: "Large"}
# Testicles, butts and breasts offer every size the source art has. Sizes that shipped before keep their old
# marking ids (LEGACY_*) so saved characters don't lose them; every other size gets a numbered/cup id.
TESTICLE_SHAPES = [  # (id fragment, display name, state prefix)
    ("", "Pair", "m_testicles_pair"),
    ("Sheath", "Sheath", "m_testicles_sheath"),
]
TESTICLE_SIZES = range(1, 9)
LEGACY_TESTICLES = {2: "Small", 4: "Average", 6: "Large"}  # pair only
BREAST_SHAPES = [  # (id fragment, display name, state prefix)
    ("", "Pair", "m_breasts_pair"),
    ("Quad", "Four", "m_breasts_quad"),
    ("Sextuple", "Six", "m_breasts_sextuple"),
]
BREAST_SIZES = range(0, 20)  # 0 is flat, then cups A to S
LEGACY_BREASTS = {2: "Small", 5: "Medium", 8: "Large", 12: "Verylarge", 16: "Huge"}  # pair only
BUTT_SIZES = range(1, 9)
LEGACY_BUTTS = {2: "Small", 4: "Average", 6: "Large", 8: "Huge"}
BELLY_SIZES = {1: "Small", 3: "Medium", 5: "Large", 7: "Very large", 9: "Huge"}
# vaginas have no visible art in the source: these are named placeholders the editor and intimacy system can see
VAGINA_TYPES = ["Tentacle", "Dentata", "Hairy", "Spade", "Feline", "Equine", "Cervine", "Sergal", "Hemi", "Furred",
                "Puffy", "Gaping", "Cloaca"]
# -----------------------------------------------------------------------------------------------------------------


def load_meta(src, rsi):
    with open(os.path.join(src, rsi, "meta.json"), encoding="utf-8") as f:
        return json.load(f)


def write_rsi(src, rsi, out_name, states):
    meta = load_meta(src, rsi)
    have = {s["name"] for s in meta["states"]}
    missing = [s for s in states if s not in have]
    if missing:
        raise SystemExit(f"{rsi}: source is missing states {missing}")
    out = os.path.join(TEX_OUT, out_name)
    if os.path.isdir(out):
        shutil.rmtree(out)
    os.makedirs(out)
    for s in states:
        shutil.copyfile(os.path.join(src, rsi, s + ".png"), os.path.join(out, s + ".png"))
    new_meta = {
        "version": 1,
        "license": meta["license"],
        "copyright": meta["copyright"] + " (subset of states)",
        "size": meta["size"],
        "states": [{"name": s, "directions": 4} for s in states],
    }
    with open(os.path.join(out, "meta.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(new_meta, f, indent=2)
        f.write("\n")


def marking(mid, category, layer, rsi, state):
    return (
        f"- type: marking\n"
        f"  id: {mid}\n"
        f"  bodyPart: {layer}\n"
        f"  markingCategory: {category}\n"
        f"  anySpecies: true\n"
        f"  followSkinColor: true\n"
        f"  forcedColoring: true\n"
        f"  sprites:\n"
        f"  - sprite: _Serenity/Genitals/{rsi}\n"
        f"    state: {state}\n"
    )


def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    src = sys.argv[1]
    yml = [
        "# GENERATED by Tools/_Serenity/gen_genital_markings.py - edit the script, not this file.\n"
        "# Art: CC-BY-SA, originally from S.P.L.U.R.T-tg; see each RSI's meta.json.\n"
        "# All follow the character's skin colour. Every species may use them, including species whose marking\n"
        "# limits are whitelist-only (anySpecies); sex is not restricted.\n\n"
    ]
    ftl = ["## GENERATED by Tools/_Serenity/gen_genital_markings.py\n\n"
           "markings-category-Breasts = Breasts\n"
           "markings-category-Penis = Penis\n"
           "markings-category-Testicles = Testicles\n"
           "markings-category-Vagina = Vagina\n"
           "markings-category-Butt = Butt\n"
           "markings-category-Belly = Belly\n\n"]

    # penises: flaccid (0) is the marking; erect (1) is copied too, ready for arousal-driven visuals later
    penis_states = []
    for frag, name, pat in PENIS_SHAPES:
        for n, size in PENIS_SIZES.items():
            flaccid, erect = pat.format(n=n, e=0), pat.format(n=n, e=1)
            penis_states += [flaccid, erect]
            mid = f"GenitalPenis{frag}{size}"
            yml.append(marking(mid, "Penis", "Penis", "penises.rsi", flaccid) + "\n")
            ftl.append(f"marking-{mid} = {name}, {size.lower()}\n")
    write_rsi(src, "penises.rsi", "penises.rsi", penis_states)
    ftl.append("\n")

    testicle_states = []
    for frag, name, prefix in TESTICLE_SHAPES:
        for n in TESTICLE_SIZES:
            st = f"{prefix}_{n}_adj"
            testicle_states.append(st)
            legacy = LEGACY_TESTICLES.get(n) if frag == "" else None
            mid = f"GenitalTesticles{legacy}" if legacy else f"GenitalTesticles{frag}{n}"
            yml.append(marking(mid, "Testicles", "Testicles", "testicles.rsi", st) + "\n")
            ftl.append(f"marking-{mid} = {name}, size {n}\n")
    write_rsi(src, "testicles.rsi", "testicles.rsi", testicle_states)
    ftl.append("\n")

    breast_states = []
    for frag, name, prefix in BREAST_SHAPES:
        for n in BREAST_SIZES:
            st = f"{prefix}_{n}_front_primary"
            breast_states.append(st)
            cup = "Flat" if n == 0 else chr(ord("A") + n - 1)
            legacy = LEGACY_BREASTS.get(n) if frag == "" else None
            mid = f"GenitalBreasts{legacy}" if legacy else f"GenitalBreasts{frag}{'Flat' if n == 0 else 'Cup' + cup}"
            yml.append(marking(mid, "Breasts", "Breasts", "breasts.rsi", st) + "\n")
            ftl.append(f"marking-{mid} = {name}, {'flat' if n == 0 else cup + ' cup'}\n")
    write_rsi(src, "breasts.rsi", "breasts.rsi", breast_states)
    ftl.append("\n")

    butt_states = []
    for n in BUTT_SIZES:
        st = f"m_butt_pair_{n}_adj_primary"
        butt_states.append(st)
        mid = f"GenitalButt{LEGACY_BUTTS.get(n, n)}"
        yml.append(marking(mid, "Butt", "Butt", "butts.rsi", st) + "\n")
        ftl.append(f"marking-{mid} = Size {n}\n")
    write_rsi(src, "butts.rsi", "butts.rsi", butt_states)
    ftl.append("\n")

    belly_states = []
    for n, size in BELLY_SIZES.items():
        st = f"m_belly_pair_{n}_front"
        belly_states.append(st)
        mid = f"GenitalBelly{size.replace(' ', '')}"
        yml.append(marking(mid, "Belly", "Belly", "bellies.rsi", st) + "\n")
        ftl.append(f"marking-{mid} = {size}\n")
    write_rsi(src, "bellies.rsi", "bellies.rsi", belly_states)
    ftl.append("\n")

    # vagina: no visible art in the source, so these are placeholder markings that exist so the anatomy can be
    # chosen in the editor and reported to the intimacy system
    write_rsi(src, "vaginas.rsi", "vaginas.rsi", ["blank"])
    yml.append(marking("GenitalVaginaStandard", "Vagina", "Vagina", "vaginas.rsi", "blank"))
    ftl.append("marking-GenitalVaginaStandard = Standard\n")
    for kind in VAGINA_TYPES:
        yml.append("\n" + marking(f"GenitalVagina{kind}", "Vagina", "Vagina", "vaginas.rsi", "blank"))
        ftl.append(f"marking-GenitalVagina{kind} = {kind}\n")

    for path, chunks in ((YML_OUT, yml), (FTL_OUT, ftl)):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("".join(chunks))
    total = len(penis_states) + len(testicle_states) + len(breast_states) + len(butt_states) + len(belly_states) + 1
    print("done:", total, "states")


if __name__ == "__main__":
    main()
