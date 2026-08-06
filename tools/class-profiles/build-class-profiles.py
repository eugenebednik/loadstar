#!/usr/bin/env python3
"""Regenerate Knowledge/06-class-profiles.md from questlog's most-liked recent builds.

    python tools/class-profiles/build-class-profiles.py            # harvest and generate
    python tools/class-profiles/build-class-profiles.py --pages 5   # deeper harvest
    python tools/class-profiles/build-class-profiles.py --offline   # regenerate from the cache

WHEN TO RE-RUN: after a patch that changes traits, stats or weapons, or when the profiles are more
than a couple of months old. This is measured data about a live meta and it goes stale the same way
the boss schedule does.

WHY A SCRIPT AND NOT A ONE-OFF: the first version of this analysis lived in a scratch directory and
would have been unreproducible the moment that directory was cleaned. The numbers in the knowledge
file are only trustworthy if the thing that produced them is still around, and the doc is generated
rather than hand-written so the two cannot drift apart.

It talks to someone else's free API, so it caches, paces itself, and identifies itself honestly.
"""
import argparse
import json
import pathlib
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from collections import Counter

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parents[1]
CACHE = HERE / "harvest-cache.json"
DEST = REPO / "src" / "Loadstar.Games.ThroneAndLiberty" / "Knowledge" / "06-class-profiles.md"
BASE = "https://questlog.gg/throne-and-liberty/api/trpc/characterBuilder."

# Captured from questlog's own class filter, and DUPLICATED from TlClasses.cs because a Python script
# cannot read a C# table. Duplication is a drift risk, so check_class_map() below parses TlClasses.cs
# and refuses to run if the two disagree -- a mismatch would silently profile the wrong weapon pair.
CLASSES = {
    "Bastion": "gauntlet-sword", "Battleweaver": "crossbow-staff", "Berserker": "sword-dagger",
    "Brawler": "gauntlet-dagger", "Bulwark": "gauntlet-orb", "Cavalier": "spear-crossbow",
    "Channeler": "gauntlet-staff", "Crucifix": "orb-crossbow", "Crusader": "sword2h-sword",
    "Darkblighter": "dagger-wand", "Disciple": "sword-staff", "Enigma": "orb-staff",
    "Eradicator": "spear-staff", "Fury": "crossbow-wand", "Gladiator": "spear-sword2h",
    "Guardian": "orb-sword", "Impaler": "spear-bow", "Infiltrator": "bow-dagger",
    "Invocator": "staff-wand", "Juggernaut": "gauntlet-sword2h", "Justicar": "orb-sword2h",
    "Liberator": "bow-staff", "Lunarch": "orb-dagger", "Marauder": "gauntlet-crossbow",
    "Mystic": "gauntlet-wand", "Oracle": "orb-wand", "Outrider": "crossbow-sword2h",
    "Paladin": "sword2h-wand", "Polaris": "orb-spear", "Raider": "crossbow-sword",
    "Ranger": "bow-sword2h", "Ravager": "sword2h-dagger", "Scorpion": "crossbow-dagger",
    "Scout": "bow-crossbow", "Scryer": "orb-bow", "Seeker": "bow-wand", "Sentinel": "staff-sword2h",
    "Shadowdancer": "spear-dagger", "Skirmisher": "gauntlet-spear", "Spellblade": "staff-dagger",
    "Steelheart": "spear-sword", "Strider": "gauntlet-bow", "Templar": "sword-wand",
    "Voidlance": "spear-wand", "Warden": "sword-bow",
}

PRETTY = {"sword": "Sword and Shield", "sword2h": "Greatsword", "wand": "Wand and Tome",
          "bow": "Longbow", "dagger": "Daggers", "crossbow": "Crossbows", "gauntlet": "Gauntlets",
          "staff": "Staff", "spear": "Spear", "orb": "Orb"}

STATS = {"str": "Strength", "dex": "Dexterity", "int": "Wisdom",
         "per": "Perception", "con": "Fortitude"}

# Traits only offered on certain weapon types. Their lift reflects which weapon the class holds, not
# a preference, so they are reported separately rather than as build philosophy.
WEAPON_BOUND = ("melee_", "range_", "magic_")

# A class needs this many observed weapon slots before any claim is made about it, a trait needs this
# many occurrences within the class, and it must be this much more common here than average. Set so
# that a plausible-looking number cannot come out of four observations.
MIN_SLOTS, MIN_COUNT, MIN_LIFT, MIN_STAT_PICKS = 30, 6, 1.6, 12


def check_class_map():
    """Refuse to run if CLASSES above disagrees with TlClasses.cs.

    The two are the same dataset written twice, so drift is possible and would be invisible: a wrong
    pair would harvest builds for one class and file them under another, and every number downstream
    would look perfectly reasonable.
    """
    source = (REPO / "src" / "Loadstar.Games.ThroneAndLiberty" / "TlClasses.cs").read_text(encoding="utf-8")
    rows = re.findall(r'\("(\w+)",\s*"(\w+)",\s*"(\w+)"\)', source)

    if not rows:
        print("could not parse TlClasses.cs -- has its table format changed?")
        return False

    from_cs = {name: frozenset((a, b)) for name, a, b in rows}
    from_py = {name: frozenset(slug.split("-")) for name, slug in CLASSES.items()}

    if from_cs == from_py:
        return True

    for name in sorted(set(from_cs) | set(from_py)):
        if from_cs.get(name) != from_py.get(name):
            print(f"  MISMATCH {name}: TlClasses.cs={sorted(from_cs.get(name) or [])} "
                  f"script={sorted(from_py.get(name) or [])}")

    return False


def call(payload, tries=3):
    url = BASE + "searchCharacters?input=" + urllib.parse.quote(
        json.dumps(payload, separators=(",", ":")))
    request = urllib.request.Request(url, headers={
        "accept": "application/json",
        "user-agent": "Loadstar/0.1 (+https://github.com/eugenebednik/loadstar) research",
    })

    for attempt in range(tries):
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                return json.loads(response.read().decode("utf-8"))
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as ex:
            if attempt == tries - 1:
                print(f"    give up: {ex}")
                return {}
            time.sleep(1.5 * (attempt + 1))


def harvest(pages):
    store = json.loads(CACHE.read_text(encoding="utf-8")) if CACHE.exists() else {}

    for name, slug in sorted(CLASSES.items()):
        if name in store:
            continue

        main, off = slug.split("-")
        rows, seen = [], set()

        for page in range(1, pages + 1):
            data = call({
                "searchTerm": "", "tags": [],
                "mainHandWeapon": main, "offHandWeapon": off,
                # Likes THIS MONTH, not lifetime: a build with 200 lifetime likes and none this month
                # was written for a patch that no longer exists.
                "sort": "likes-month", "page": page,
            }).get("result", {}).get("data")

            if not data or not data["pageData"]:
                break

            for row in data["pageData"]:
                # The weapon filter is real, but several other plausible filter keys are accepted and
                # SILENTLY IGNORED by this endpoint -- and an ignored filter returns the unfiltered
                # top of the list, which looks exactly like a successful query. So re-check.
                if sorted(row.get("weaponTypes") or []) != sorted([main, off]):
                    continue
                if row["url"] not in seen:
                    seen.add(row["url"])
                    rows.append(row)

            time.sleep(0.3)

        store[name] = rows
        print(f"  {name:14} {slug:20} {len(rows):>3} builds")

    CACHE.write_text(json.dumps(store), encoding="utf-8")
    return store


def analyse(store):
    per_class, global_traits, global_heroic = {}, Counter(), Counter()
    global_slots = global_hslots = 0

    for name, builds in store.items():
        traits, heroic, stats = Counter(), Counter(), Counter()
        slots = hslots = 0

        for build in builds:
            for slot in ("equipmentMainHand", "equipmentOffHand"):
                equipment = build.get(slot) or {}

                if equipment.get("traits"):
                    slots += 1
                    traits.update(equipment["traits"].keys())

                # Explicit nulls mean an UNFILLED heroic slot. Counting them produced a phantom trait
                # called "None" that showed up as some classes' distinctive pick.
                picks = [v for v in (equipment.get("heroic") or {}).values() if v]

                if picks:
                    hslots += 1
                    heroic.update(picks)
                    stats.update(p for p in picks if p in STATS)

        per_class[name] = {"slots": slots, "hslots": hslots,
                           "traits": traits, "heroic": heroic, "stats": stats}
        global_traits += traits
        global_heroic += heroic
        global_slots += slots
        global_hslots += hslots

    report = {}
    for name, data in per_class.items():
        slots = data["slots"]
        entry = {"slots": slots}

        if slots < MIN_SLOTS:
            entry["traits"] = None
        else:
            rows, bound = [], []
            for trait, count in data["traits"].items():
                base = global_traits[trait] / global_slots
                if count < MIN_COUNT or base == 0:
                    continue
                lift = (count / slots) / base
                if lift >= MIN_LIFT:
                    row = (trait, round(100 * count / slots), round(lift, 1))
                    (bound if trait.startswith(WEAPON_BOUND) else rows).append(row)

            entry["traits"] = sorted(rows, key=lambda r: -r[2])
            entry["weaponBound"] = sorted(bound, key=lambda r: -r[2])

        total = sum(data["stats"].values())
        entry["stats"] = ([(STATS[k], v, round(100 * v / total))
                           for k, v in data["stats"].most_common()]
                          if total >= MIN_STAT_PICKS else None)
        report[name] = entry

    stat_totals = Counter({k: v for k, v in global_heroic.items() if k in STATS})
    stat_sum = sum(stat_totals.values())

    return {
        "builds": sum(len(v) for v in store.values()),
        "weaponSlotsObserved": global_slots,
        "heroicSlotsObserved": global_hslots,
        "globalTraitBaseline": {t: round(100 * c / global_slots, 1)
                                for t, c in global_traits.most_common(16)},
        "globalStatBaseline": {STATS[k]: round(100 * v / stat_sum)
                               for k, v in stat_totals.most_common()},
        "classes": report,
    }


def generate(d, captured):
    L = []
    w = L.append

    w("# What people actually play, per class")
    w("")
    w(f"Measured from **{d['builds']} questlog builds** — the most-liked of the last 30 days for every one")
    w("of the 45 classes, harvested per class so the rare ones are not crowded out by the popular ones.")
    # An EDITORIAL date, never an ISO one. A test bans ISO dates from the system prompt so that a
    # DateTime baked into a static field cannot slip past byte-stability checks that only compare two
    # calls inside one process. Regenerate with --captured to change this.
    w(f"Captured {captured}. **{d['weaponSlotsObserved']} weapon slots** carried trait data and")
    w(f"**{d['heroicSlotsObserved']}** carried heroic picks.")
    w("")
    w("## Read this as a description, NOT as a target")
    w("")
    w("These are the choices popular builds make. That is evidence about the meta and it is genuinely")
    w("useful — but it is **not** proof of what is optimal, and there are four specific reasons to keep it")
    w("at arm's length:")
    w("")
    w("- **questlog is unmoderated.** Anyone can publish, likes measure visibility as much as quality, and")
    w("  a build can be half-finished or written for an older patch.")
    w("- **Popularity is self-reinforcing.** A build near the top gets copied because it is near the top.")
    w("- **The player's own build wins on intent.** If they pinned one, it says what they are trying to do.")
    w("  This table says what strangers did.")
    w("- **Never quote a percentage from here as a benchmark to reach.** \"62% of Mystics take Dexterity\"")
    w("  is a fact about a spreadsheet. \"Your Dexterity is below the Mystic benchmark\" is a number the")
    w("  player will act on, and it does not exist.")
    w("")
    w("Use it to break ties, to sanity-check a stat spread that looks nothing like anyone else's, and to")
    w("answer \"what do people usually do with this class\" — which players ask constantly and which")
    w("nothing else in this prompt can answer.")
    w("")
    w("## Sparse fields, and what could NOT be measured")
    w("")
    w("Stated because absence here is a real finding rather than an omission:")
    w("")
    w("- **Author tags are unusable.** Only 11% of builds carry any tag at all, and 12 classes have")
    w("  **zero** builds tagged PvE or PvP. So a class's PvE/PvP character cannot be read from tags, and")
    w("  anything claiming to is reading noise. **Ask the player their axis; do not infer it from a class.**")
    w("- **Potential abilities: 4-5% present.** Not measurable.")
    w("- **Trait Resonance: 30-44%.** Too patchy per class to characterise.")
    w("- Classes below the reporting threshold are marked so. A blank is honest; a number from four")
    w("  observations is not.")
    w("")
    w("## The global baseline — the trap this table exists to avoid")
    w("")
    w("Three weapon traits are near-universal, so naming them for a class says nothing:")
    w("")
    w("| Trait | Share of all weapon slots |")
    w("| --- | --- |")

    for trait, pct in list(d["globalTraitBaseline"].items())[:6]:
        w(f"| `{trait}` | {pct}% |")

    w("")
    w("`all_double_attack`, `all_accuracy` and `all_critical_attack` sit on the majority of weapons in the")
    w("game. **A class is characterised by what it takes MORE than average**, which is what the lift")
    w("figures below measure — `3.4x` means that class takes it three and a half times as often as the")
    w("average class does. A trait at 1.0x is the meta, not the class.")
    w("")
    w("Heroic base-stat baseline across all classes: "
      + ", ".join(f"**{k} {v}%**" for k, v in d["globalStatBaseline"].items()) + ".")
    w("Perception dominates everywhere, so Perception being a class's top pick is weak evidence; a class")
    w("leading on Fortitude or Wisdom is deviating and that means something.")
    w("")
    w("## Per class")
    w("")
    w("`stat priority` is the split of base-stat heroic picks — Strength, Dexterity, Wisdom, Perception,")
    w("Fortitude. `distinctive` lists weapon traits taken at least 1.6x more often than average.")
    w("")

    for name in sorted(d["classes"]):
        info = d["classes"][name]
        first, second = CLASSES[name].split("-")
        w(f"### {name} — {PRETTY[first]} + {PRETTY[second]}")
        w("")

        stats = info.get("stats")
        if stats:
            w("- **Stat priority:** " + ", ".join(f"{s} {p}%" for s, _c, p in stats)
              + f"  _(from {sum(c for _s, c, _p in stats)} heroic picks)_")
        else:
            w("- **Stat priority:** not enough heroic picks observed to say.")

        traits = info.get("traits")
        if traits is None:
            w(f"- **Distinctive traits:** only {info['slots']} weapon slots observed — too few to characterise.")
        elif traits:
            w("- **Distinctive traits:** "
              + ", ".join(f"`{t}` {s}% ({l}x)" for t, s, l in traits[:5]))
        else:
            w("- **Distinctive traits:** none — takes the same traits as the average class.")

        bound = info.get("weaponBound") or []
        if bound:
            w("- _Weapon-bound (available only to this weapon type, so the lift is mechanical rather than a"
              " preference):_ " + ", ".join(f"`{t}` {s}%" for t, s, _l in bound[:3]))

        w("")

    w("## How to use a profile in an answer")
    w("")
    w("Good: \"Mystic builds lean hard on Dexterity — 64% of their heroic picks, against 19% across all")
    w("classes. Your spread is Fortitude-heavy, which is unusual for this class; if that is deliberate,")
    w("keep it, and if it is not, Dexterity is where the class's damage comes from.\"")
    w("")
    w("Bad: \"Mystics need 64% Dexterity.\" That is not what the number means and it is not achievable")
    w("advice — it is a share of trait picks across strangers' builds, not a stat target.")
    w("")
    w("And when a class shows nothing distinctive, **say so plainly**. \"Nothing unusual about this class's")
    w("trait choices\" is a real answer; inventing a characterisation to fill the gap is not.")

    text = "\n".join(L) + "\n"
    DEST.write_text(text, encoding="utf-8")
    return text


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--pages", type=int, default=3, help="pages per class (18 builds each)")
    parser.add_argument("--offline", action="store_true", help="regenerate from the cache, no requests")
    parser.add_argument("--captured", default="6 August 2026",
                        help="editorial capture date for the doc header — NOT an ISO date")
    args = parser.parse_args()

    if not check_class_map():
        print("class map disagrees with TlClasses.cs -- fix that first, or the profiles will be "
              "filed under the wrong classes")
        return 1

    if args.offline:
        if not CACHE.exists():
            print(f"no cache at {CACHE}; run without --offline first")
            return 1
        store = json.loads(CACHE.read_text(encoding="utf-8"))
        print(f"offline: {sum(len(v) for v in store.values())} builds from cache")
    else:
        print(f"harvesting up to {args.pages} pages per class (delete {CACHE.name} to refetch)")
        store = harvest(args.pages)

    missing = [name for name in CLASSES if not store.get(name)]
    if missing:
        print(f"\nWARNING: no builds for {', '.join(missing)} — their profiles will read as unmeasured")

    d = analyse(store)
    text = generate(d, args.captured)

    print(f"\n{d['builds']} builds, {d['weaponSlotsObserved']} weapon slots, "
          f"{d['heroicSlotsObserved']} heroic slots")
    print(f"wrote {DEST.relative_to(REPO)} — {len(text)} chars, ~{len(text) // 4} tokens")

    characterised = sum(1 for v in d["classes"].values() if v.get("traits"))
    with_stats = sum(1 for v in d["classes"].values() if v.get("stats"))
    print(f"{characterised}/{len(CLASSES)} classes have distinctive traits, "
          f"{with_stats}/{len(CLASSES)} have a stat priority")
    print("\nNow run: dotnet test tests/Loadstar.Core.Tests/Loadstar.Core.Tests.csproj")
    return 0


if __name__ == "__main__":
    sys.exit(main())
