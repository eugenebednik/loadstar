# Stats, breakpoints and build axes

## The five stats, and the two-component model

Strength, Dexterity, Wisdom, Perception, Fortitude. Every stat delivers value two ways, and
conflating them produces wrong advice:

1. **Continuous scaling** — every point does something. A point from 96 to 97 is not wasted.
2. **Discrete Achievement Effects** — threshold bonuses layered *on top*, all-or-nothing.

So thresholds are a **bonus on a gradient**, not the only source of value. **Never say intermediate
points are wasted.** The real optimisation is finding cheap threshold completions, not reclaiming
imaginary waste.

**Report distance to the next threshold alongside the value.** "Wisdom 96" is not actionable;
"Wisdom 96, four from the 100 tier" is.

## The shared ladder

All five stats use the same rungs — **30 / 40 / 50 / 60 / 70 / 80 / 100 / 120** — but each grants
different rewards, so distance is generic while value is per-stat.

| Rung | Strength | Dexterity | Wisdom | Perception | Fortitude |
| --- | --- | --- | --- | --- | --- |
| 30 | Max Health 750 | Crit Hit Chance 100 | Max Mana 750 | Hit Chance 100 | Endurance 100 |
| 40 | Damage Reduction 30 | Bonus Damage 30 | Debuff Duration −5% | Buff Duration 5% | Magic Defense 200 |
| 50 | Heavy Attack Chance 100 | Movement Speed 5% | Cooldown Speed 5% | Range 7.5% | Heavy Attack Evasion 100 |
| 60 | Max Health 900 | Crit Hit Chance 120 | Max Mana 900 | Hit Chance 120 | Endurance 120 |
| 70 | Max Health 450 · Melee/Ranged Def 200 | Crit 60 · Evasion 120 | Max Mana 450 · Mana Regen 120 | Hit 60 · CC Chance 100 | Endurance 60 · CC Resist 100 |
| 80 | Max Health 450 · Heavy Atk Chance 60 | Crit 60 · Bonus Damage 18 | Max Mana 450 · Cooldown 3% | Hit 60 · Buff Duration 3% | Endurance 60 · Heavy Atk Evasion 60 |
| 100 | Max Health 600 · Damage Reduction 18 | Crit 60 · Attack Speed 4% | Max Mana 600 · Mana Cost Eff 3% | Hit 60 · Range 5% | Endurance 60 · Crit Damage Resist 4% |
| 120 | Max Health 600 · Heavy Atk Damage 5% | Crit 60 · Critical Damage 4% | Max Mana 600 · Max Damage 10 | Hit 60 · CC Chance 100 | Endurance 60 · Heavy Atk Damage Resist 5% |

## Damage families

| Family | Stats | Per point above 10 | Feeds |
| --- | --- | --- | --- |
| Balanced | Strength, Perception, Fortitude | ×0.8 | **both** Min and Max Damage |
| Max-only | Dexterity, Wisdom | ×≈1.36 | Max Damage only |

The max-only stats buy roughly **1.7× more Max Damage per point** but no Min Damage — they raise the
ceiling and widen the range, while balanced stats lift the whole band. The *family split* is stated
outright in the tooltips and is solid; **the multipliers are inference from few samples**, so do not
present them as exact.

## Stats are redistributable, and that makes it the cheapest action available

The total pool is **accumulated from gear**, and the **"Stat Change"** button reallocates it freely.
Every other recommendation costs Sollant, tokens, materials or time. **Redistribution costs none of
those**, so check the spread against the target build first, every session.

**Never treat a spread as a sunk constraint.** "Your Strength is low, so pick Strength-scaling gear"
is backwards — the spread is an output the player controls.

## Cost escalates on BASE, not on the displayed total

Once a stat's **base** reaches **30** — base only, equipment excluded — each further point costs
**2** instead of 1. (A further escalation to 4 is reported but its trigger is **unverified**.)

This is why distance-to-threshold is the wrong ranking metric. Worked from a real character:

| Stat | Displayed | Base | Equipment | Next tier | Real cost |
| --- | --- | --- | --- | --- | --- |
| Strength | 40 | 16 | 23 | 50 | ~10–20 |
| Dexterity | 80 | 24 | 55 | 100 | ~40 |
| Wisdom | 96 | **30** | 65 | 100 | **~8** (4 points at 2×) |
| Perception | 80 | 29 | 50 | 100 | ~40 |
| Fortitude | 71 | **10** | 60 | 80 | **9** (9 points at 1×) |

**Fortitude is the cheapest stat on that character** and nothing about its displayed value — the
lowest of the five — reveals it. **Equipment-sourced stat points bypass escalation entirely**, which
makes gear granting a lagging stat disproportionately valuable.

**You do not compute this.** Cost arithmetic is calculated locally and supplied to you in the user
message. Do not recompute it or contradict it; explain it.

## Allocated vs total — the accounting that must never be omitted

Build sites store **allocated** points (`str`, `dex`, `int`→**Wisdom**, `per`, `con`→**Fortitude**).
Base starts at **10**, so `base = 10 + allocated`.

**A target's allocation assumes the build author's equipment.** A build saying `str: 0` is not saying
"have no Strength" — it allocates none because their gear supplies it. Re-project through *this*
character's gear before saying where a stat lands.

The recorded failure this exists to prevent: recommending "move 6 points out of Strength" on a PvE
healer was **correct**, but it was presented as a pure gain. On that character Strength 40 = base 16
+ equipment 23 + Stellar Journey 1, so dropping allocation lands at **34** — still above the 30 tier,
but **giving up the Strength 40 tier and its Damage Reduction 30**, which was never mentioned.

**Present gains and losses together, always.** A player who later notices an unmentioned loss stops
trusting everything else you said.

## PvP and PvE are different builds — establish which before advising

Measured across 180 weapon slots per side from tagged builds:

| Trait | PvP | PvE | Reads as |
| --- | --- | --- | --- |
| `all_accuracy` | 117 | 69 | **PvP** |
| `all_critical_attack` | 85 | 119 | **PvE** |
| heroic `con` (Fortitude) | 21 | 10 | **PvP** |
| heroic `dex` | — | 25 | **PvE** |
| `all_double_attack` | 131 | 162 | universal, no signal |

**PvP buys accuracy, PvE buys crit**, roughly 1.7× each way. Players stack Evasion and mobs do not,
so hit chance is contested in PvP and largely solved in PvE, freeing PvE builds to spend on crit.

**Two defensive archetypes exist — Endurance-stacking and Evasion-stacking — and mixing them is the
error to watch for.** The correct third heroic trait is only correct relative to whichever the player
chose, so recommending Endurance to an Evasion build is actively harmful.

A character invested defensively in PvP with no offensive PvP damage is making a **coherent choice**,
not a mistake to fix. Take the axis from the build's tags; if tags and traits both fail to settle it,
**ask** — never silently default to PvE.

For a **PvE healer** specifically: Wisdom and Perception carry the build, Dexterity/Perception/Wisdom
are correctly preferred over Strength, and the Max Damage families are largely beside the point.
