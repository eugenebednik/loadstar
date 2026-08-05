# T3 versus T4 — when a higher-tier item is actually an upgrade

**This is the most common way to hand a player a worse item.** New and returning players are given
**fully-traited T3 gear for free**. A T4 drop does not automatically beat it, and below the
crossover it is a downgrade — the free T3 has its traits unlocked and levelled, while a T4 piece
arrives **bare**, because 4.0.0 removed traits from dropped gear entirely.

T3 item stat tables run to **level 50**. T4 tables begin at **51** and run to **80**.

## The crossover, measured

Median primary stat per slot, taken across every item in each tier band in the live catalogue on
patch 4.5.0. Percentages are against a **maxed level-50 T3** piece of the same slot.

*(No verification date here on purpose — this file is part of the cached prompt prefix, and anything
date-shaped in it is indistinguishable from a rendered clock, which would invalidate the cache on
every request. The date this was measured lives in CLAUDE.md.)*

| Slot | T3 items | T4 items | T3 @50 | T4 @51 | T4 @61 | T4 @71 | T4 @80 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `belt` | 20 | 7 | 430 | 370 (86%) | 420 (98%) | 470 (109%) | 515 (120%) |
| `bow` | 8 | 6 | 1760 | 1680 (95%) | 1680 (95%) | 1680 (95%) | 1680 (95%) |
| `bracelet` | 21 | 7 | 430 | 370 (86%) | 420 (98%) | 470 (109%) | 515 (120%) |
| `chest` | 27 | 12 | 400 | 360 (90%) | 410 (102%) | 460 (115%) | 505 (126%) |
| `cloak` | 13 | 5 | 325 | 325 (100%) | 375 (115%) | 425 (131%) | 470 (145%) |
| `crossbow` | 9 | 6 | 2800 | 2600 (93%) | 2600 (93%) | 2600 (93%) | 2600 (93%) |
| `dagger` | 9 | 6 | 2400 | 2300 (96%) | 2300 (96%) | 2300 (96%) | 2300 (96%) |
| `earring` | 17 | 7 | 430 | 370 (86%) | 420 (98%) | 470 (109%) | 515 (120%) |
| `feet` | 27 | 12 | 380 | 340 (89%) | 390 (103%) | 440 (116%) | 485 (128%) |
| `gauntlet` | 8 | 6 | 2950 | 1850 (63%) | 2350 (80%) | 2850 (97%) | 3300 (112%) |
| `hands` | 27 | 12 | 380 | 340 (89%) | 390 (103%) | 440 (116%) | 485 (128%) |
| `head` | 27 | 12 | 390 | 350 (90%) | 400 (103%) | 450 (115%) | 495 (127%) |
| `legs` | 27 | 12 | 390 | 350 (90%) | 400 (103%) | 450 (115%) | 495 (127%) |
| `necklace` | 19 | 8 | 430 | 370 (86%) | 420 (98%) | 470 (109%) | 515 (120%) |
| `orb` | 8 | 6 | 1320 | 1320 (100%) | 1320 (100%) | 1320 (100%) | 1320 (100%) |
| `ring` | 38 | 14 | 430 | 370 (86%) | 420 (98%) | 470 (109%) | 515 (120%) |
| `spear` | 8 | 6 | 709 | 642 (91%) | 642 (91%) | 642 (91%) | 642 (91%) |
| `staff` | 9 | 6 | 1760 | 1680 (95%) | 1680 (95%) | 1680 (95%) | 1680 (95%) |
| `sword` | 11 | 6 | 2650 | 2100 (79%) | 2600 (98%) | 3100 (117%) | 3550 (134%) |
| `sword2h` | 10 | 6 | 709 | 624 (88%) | 624 (88%) | 624 (88%) | 624 (88%) |
| `wand` | 8 | 6 | 1540 | 1610 (105%) | 1610 (105%) | 1610 (105%) | 1610 (105%) |

## What it means, and how to use it

For **armour and accessories** the pattern is consistent and matches what players report:

- **Level 51–60 — T4 is WORSE.** Around 86–90% of maxed T3 on raw stats, before you account for the
  T3 piece already being fully traited. Do not recommend the swap.
- **Level 61 — parity.** 98–103%. The raw stats have caught up; whether it is an upgrade now depends
  entirely on how well-traited the T3 piece is.
- **Level 71+ — genuinely better.** 109–116%, enough to overcome a traited T3.
- **Level 80 — 120–128%.**

So the rule to state plainly: **a T4 piece is not an upgrade over established T3 until roughly
level 61, and not clearly one until 71.**

Two exceptions in the table worth knowing:

- **`cloak` is already at parity at 51** (100%) and pulls ahead fastest of anything (145% at 80).
- **`gauntlet` is the worst offender** — only 63% at level 51, and it does not reach parity until
  about level 71. A gauntlet swap below 71 is a serious downgrade.

**Most weapons do not scale with item level at all.** Bow, crossbow, dagger, staff, spear, sword2h,
orb and wand show a flat value across the whole 51–80 range, so for those the level number tells you
nothing about base damage — the difference lives in traits, runes and unlocks instead. Only `sword`
and `gauntlet` scale. Do not tell a player their bow got stronger because its item level rose.

## The action this changes

A T4 piece below the crossover is still worth taking — as **Succession fuel**, not as something to
wear. That is a different recommendation with a different action attached, and saying "this is a
downgrade to equip but good material to inherit from" is far more useful than either "upgrade!" or
"ignore it".
