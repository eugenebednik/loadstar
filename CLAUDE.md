# Loadstar — project knowledge

Read this before changing anything in `src/`. It carries the two things that aren't derivable
from the code: the anti-cheat contract, and what the game actually looks like on screen.

## The one rule

**Loadstar observes. It never touches the game.** No injection, no renderer hooking, no
process memory, no synthetic input. Full contract in
[docs/anti-cheat-posture.md](docs/anti-cheat-posture.md) — a PR that breaks it gets rejected
even if the feature is good.

That rule is why several things below are harder than they look. Every "why don't we just hover
over it" idea is answered by "because hovering is input".

---

# Throne and Liberty

Verified against a live client on **2026-08-03**, patch **4.5.0**. Re-verify after any patch;
the item system in particular was rewritten wholesale in 4.0.0 and older guides are actively
misleading.

## The screen-reading problem

This is the central technical constraint of the whole project.

**Neither the currency bar nor the inventory shows text labels.** Both render as icons with
numbers. Names exist only in hover tooltips, and hovering is input, which is forbidden.

| Surface | What a screenshot actually contains |
| --- | --- |
| Currency bar (top strip, full width) | 8 icons, 8 numbers. No names. Collapsed by default — shows Lucent only. |
| Inventory (right panel) | Icon grid with stack counts, rarity as border colour, capacity `101/160`. No names. |
| Character sheet (full-screen) | **The good one.** Named stats with values, and an item-level number on every equipment slot. |

**Panels are draggable.** Inventory and most windows can be moved anywhere, so fixed crops are
unsafe — a crop that misses the panel costs the same tokens and returns nothing. Only the
currency bar is edge-anchored and safe to crop. Everything else: capture the full window and
let the model locate the panel. It's good at that; it's bad at naming icons.

## The character sheet is the highest-value capture

Prefer it over everything else. It is full-screen, text-rich, and carries the three numbers
that actually drive progression advice:

- **Item level per equipment slot** — a number on every slot (72, 75, 71, 72, **50**, 73 …).
  This is what a questlog target build compares against directly. A slot sitting at 50 while
  its neighbours are 72+ is a concrete, actionable gap, and finding those is most of the job.
- **Gear Score** (e.g. `8,703`) — the headline aggregate. Useful for tracking session-over-
  session movement, and for content gating.
- **Equipment Watermark** (e.g. `73`) — the highest item level obtained for a slot, which sets
  the floor for future drops. This is why it matters for advice: raising the watermark improves
  everything you will be given later, so watermark-raising actions can be worth more than a
  single upgrade that looks bigger right now. Advice that ignores the watermark will
  systematically undervalue them.

Stat points assigned sit just above these, alongside the named stats (Strength, Dexterity,
Wisdom, Perception, Fortitude), each with a value and remaining unspent points.

## The Combat Power tooltip is the single best input in the game

**Hovering the gear score opens a tooltip that breaks Combat Power into current/max per
category.** Observed 2026-08-03:

| Category | Current / Max | Headroom |
| --- | --- | --- |
| Levels | 350 / 350 | 0 (maxed) |
| Skills | 728 / 840 | 112 |
| Weapon Masteries | 1047 / 1400 | 353 |
| Weapons | 1248 / 1464 | 216 |
| Armor | 1650 / 2089 | 439 |
| Accessories | 1850 / 2357 | 507 |
| Artifacts | 380 / 380 | 0 (maxed) |
| Runes | 988 / 1530 | **542** |
| Stellar Journey | 462 / 524 | 62 |

It also reports Maximum Combat Power and the date it was achieved.

This is a **ranked list of remaining headroom, computed by the game itself**. It converts the
core question — "where does my next hour best go?" — from an inference problem into a lookup.
On this character it says plainly: Runes are the largest single gap, then Accessories, then
Armor; Levels and Artifacts are finished and any advice to push them is wasted effort.

**Prefer this over any heuristic we could invent.** If the tooltip is available, category
priority comes from it, not from our own reasoning about item levels. Item level per slot then
answers the follow-up: *which piece* within the winning category.

**But Loadstar cannot open it.** It is a hover tooltip, and hovering is input — forbidden. So
this is a **user-initiated capture**: the player hovers the gear score and presses the capture
hotkey. Treat it like the named-currency reference — prompt for it on first run, and again
periodically, because the numbers move as gear changes.

## Equipment Level (watermark) — the average rule, and why it inverts advice

Hovering the watermark gives its definition verbatim:

> This level serves as the standard for the level of equipment obtained in the future. It is
> determined by the average of the highest levels of each type of equipment you have obtained:
> weapons, armor, and accessories.

Observed 2026-08-03 — **Equipment Lv. 73**, from Max Weapon **73**, Max Armor **74**, Max
Accessory **73**. That is `(73 + 74 + 73) / 3 = 73.33`, floored to 73.

Three consequences, and they matter because they invert what looks obvious:

1. **It is an average of three category maxima, not a single number.** The watermark rises only
   when the *average* crosses the next integer, which in practice means the **lagging**
   categories have to come up.
2. **Upgrading your strongest category is wasted for watermark purposes.** On this character
   Armor is already at 74 while Weapon and Accessory sit at 73. Another Armor upgrade moves the
   watermark by nothing. Only Weapon or Accessory does.
3. **Only the single highest item ever obtained in each category counts** — not the average of
   what's equipped, and not what's currently worn. An item obtained and then replaced still
   counts.

This directly contradicts the naive reading of the Combat Power tooltip. There, Armor showed a
large headroom (439) and looks like a priority. For raising the *watermark*, Armor is the one
category that cannot help. Good advice reconciles the two rather than quoting whichever it saw
first: use Combat Power headroom for raw power, and the watermark average for what improves
every future drop.

Because the watermark floors future gear, moving it is often worth more than a single larger
upgrade elsewhere — advice that ignores it will systematically undervalue lagging-category
upgrades.

So **do not ask the vision model to name items from icons.** It will produce plausible names
that are wrong, and wrong item names produce confidently wrong spending advice.

Two mechanisms replace guessing:

1. **Named-currency reference capture (one-time).** The user opens the full currency window
   once — it lists each currency by name beside its icon — and captures it. That image is
   stored and paired with every later currency reading, turning identification into a lookup.
2. **Local icon matching for inventory.** questlog's item catalogue
   (`characterBuilder.getPreviewEquipmentItems`) carries item ids and icon asset paths. Build a
   local icon index once, then match inventory tiles against it with perceptual hashing before
   the model ever sees them. This is deterministic, free, and offline — strictly better than
   asking a vision model to recognise a 40px icon.

The model's job is reading **numbers and layout**, which it is reliable at. Identification is
ours.

## Expanded character info — the definitive stat sheet

The expanded view of the character window is **fully text-labelled** and is the authoritative
read on where the character actually stands. Three columns plus a tabbed right-hand panel,
observed 2026-08-03:

- **Weapons** — Base Damage 295~636, Attack Speed 0.312s, Range 30m
- **Defense** — Melee 3,370, Magic 3,957, Ranged 3,163
- **Attack** — hit chance, critical hit chance and heavy attack chance across melee/ranged/magic,
  plus Skill Damage Boost 202 and Bonus Damage 48
- **Protection** — evasion, endurance and heavy attack evasion across all three, plus Skill
  Damage Resistance 771 and Damage Reduction 36
- **Right panel** (eight category tabs) — Max Health 36,009, Max Mana 16,692, Max Stamina 100,
  regen values, Mana Cost Efficiency +48.8%, Cooldown Speed +120%, Movement Speed 745, Attack
  Speed +59.96%, Healing +205.61%, Shield Health +107.4%, Amitoi Healing +637.28%, Potion
  Healing +127%, Range +65.1%, and more

**This is what a questlog target build's stat goals compare against**, so it is the natural
partner to the build import: the build states the target, this screen states the actual.

Two capture caveats: the left columns **scroll** (Bonus Damage and Damage Reduction are cut off
at the fold), and the right panel is **tabbed** — so a complete stat picture needs more than one
capture. Ask for the specific tab that matters rather than trying to collect all of them.

### The right-hand tabs (7 total)

The left three columns stay fixed while the right panel swaps. Captured 2026-08-03:

**Tab 1 — Vitals and modifiers.** Max Health 36,009, Max Mana 16,692, Max Stamina 100 and their
regen values; Mana Cost Efficiency +48.8%, Cooldown Speed +120%, Movement Speed 745, Attack
Speed +59.96%, Healing +205.61%, Skill Healing over Time +72.38%, Shield Health +107.4%, Amitoi
Healing +637.28%, Potion Healing +127%, Range +65.1%, plus received/incoming counterparts.

**Tab 2 — Crowd control.** Eight effects, each a Chance/Resistance pair: Weaken (568 / 755.8),
Stun (524 / 589), Petrification (524 / 564), Sleep (458 / 564), Silence (503 / 539), Fear
(524 / 543), Bind (524 / 564), Collision (524 / 564).

**Tab 3 — Species.** Five species — **Humanoid, Undead, Wildkin, Construct, Demon** — each with
four values: Damage Boost, Bonus Damage, Damage Resistance, Damage Reduction. Example: Humanoid
101.4 / 10 / 66 / 8; Demon 101.4 / 10 / 66 / 32.

**Tab 4 — Directional.** Front / Side / Back variants of Hit Chance, Critical Hit, Heavy Attack
Chance and Bonus Damage on the left; Evasion, Endurance, Heavy Attack Evasion and Damage
Reduction on the right. Observed almost entirely zero, with three exceptions: Back Hit Chance
**−15.4**, Front Critical Hit Chance 6, Front Heavy Attack Chance 5.4, Side Heavy Attack Chance
7, Front Endurance 185.

Two things matter here. **Directional stats can be negative** — the hat tooltip carried a
`Back Hit Chance −21` PvP modifier, and it shows up in the aggregate. And a panel of mostly
zeros is not noise, it is **untapped headroom**: this is an advanced optimisation axis the
player has not engaged with, which is worth knowing before recommending it as a priority.

Tabs 5–7 not yet captured.

**Species pairs with the boss timer.** Monsters and bosses belong to these species, so knowing
which species the next world boss is turns a generic suggestion into a timed, specific one —
"swap to Demon damage before the 21:00 spawn". Worth wiring the boss schedule's species tag
into the advice prompt.

## Unlocking equipment stats raises gear score *and* character stats

The `Locked` entries on an item tooltip are unclaimed stat slots. Unlocking them feeds both the
gear score aggregate and the character stats above — so an item's contribution is not fixed at
the item level printed on its slot.

This is why item level alone under-describes a piece, and why the advice engine must treat
unlock actions as first-class progression steps rather than optional polish. A slot at 72 with
two locked entries has real headroom that a slot at 72 with everything filled does not, and
only the tooltip shows the difference.

## Item tooltips — the richest source, and fully readable

Hovering an equipment slot gives a tooltip that is **completely text-labelled**, unlike every
icon grid in the game. Observed 2026-08-03 on one headgear slot:

- **Name, rarity, slot, item level** — `Frigid Melody Hat` / `Epic | Headgear` / `Item Level 72`
- **Base defences** — Melee Defense 432, Ranged Defense 387
- **Rolled stats** — Wisdom 7, Strength 7, Heavy Attack Evasion 152, Mana Cost Efficiency
  +13.2%, Max Health 890
- **Traits, with fill pips and locked slots** — `Cooldown Speed +6%` at 4 pips, then two entries
  reading `Locked`
- **Rune-style lines with levels** — Back Hit Chance 5.6 (Lv. 8), Species Damage Boost 2.2
  (Lv. 11), plus PvP-specific modifiers and a `Synergy:` line
- **Set progress and set bonuses** — `Frigid Melody Set (4/5)` with per-piece item levels, and
  the 2-piece and 4-piece effects spelled out
- **Sale Price** (109,090) and material type (Cloth)
- Two hotkey hints: **`Alt+C` View Max Item Level Value** and **`Alt+G` Switch to Detailed View**

Four things follow:

1. **This is the ground truth for gear.** Anything derived from an icon is a guess; a tooltip
   capture is authoritative. When precision matters, ask for the tooltip.
2. **Locked trait slots are a progression lever** the item-level number alone never reveals.
   A 72 with two locked slots has headroom a 72 with three filled slots does not.
3. **Set completion is a discrete cliff.** `(4/5)` means one piece away from a bonus — worth
   far more than a marginal item-level bump elsewhere, and invisible without the tooltip.
4. **`Alt+C` shows the item's maximum item level value**, which is exactly the current-vs-
   ceiling comparison the advice engine wants. Worth prompting the user to capture.

The cost is that tooltips need hovering, which is input, so they are user-initiated captures.
Thirteen slots is too many to ask for routinely — so: local icon matching for bulk inventory,
tooltip captures for the handful of slots actually under consideration.

## Currencies — left to right along the top bar

Authoritative; supplied by a player of the account, then cross-checked. **Only two of these
are relevant to gear progression.** The assistant must never recommend spending the others on
upgrades, and must never treat them as fungible with Sollant.

| # | Currency | What it is | Use for progression? |
| --- | --- | --- | --- |
| 1 | **Lucent** | Premium currency, bought with real money via the cash shop. Also the auction-house trading currency. | **Real money.** Never recommend spending it as if it were earned. Flag the cost in dollars. |
| 2 | **Sollant** | The game's gold. Earned in play. Crafting, upgrades, merchants, most sinks. | **Yes — the primary progression currency.** |
| 3 | **Contract Coin** | Earned from contracts. Spent at the Contract Merchant. | **Yes** — merchant-gated, so advice must name the merchant. |
| 4 | **Guild Coin** | Earned through guild activity. Spent at the Guild Merchant. | **Yes** — guild merchant only. |
| 5 | **Restoration Coin** | Restores a downed character on death. | **No.** Never a progression input. Do not suggest saving or spending it for gear. |
| 6 | **Ornate Coin** | Compensation grants from the developers for downtime, plus exploration rewards. Buys cosmetics, and some boosts (e.g. runes) in the cash shop. | **Partially** — the rune boosts are the only progression-relevant sink. Cosmetics are not progression. |
| 7 | **Loyalty Points** | Accrued by spending real money: **1,000 points per $99.99 spent.** Buys rare and best-in-slot **cosmetics**. | **No.** Cosmetic only. Never present as a gear path. |
| 8 | **Character Boost Ticket** | **$49.99 each**, purchasable repeatedly. Boosts a new character to level 55. | **No.** A real-money alt-levelling item, not a progression currency. |

Three rules that fall out of this table and must be enforced in the system prompt:

- **Never recommend a real-money purchase as a progression step.** Lucent, Loyalty Points, and
  Boost Tickets are dollars. If a path genuinely requires them, say the dollar cost plainly and
  present it as optional.
- **Restoration Coin is not a resource to optimise.** It has exactly one use and it isn't gear.
- **Loyalty Points and most Ornate Coin spending buy cosmetics.** Cosmetics are not progress.

## Current progression model (4.0.0 onwards)

**Update 4.0.0 "The Frozen Divide: Nix" (2026-06-25)** rewrote item progression. Anything
written before that date is wrong about the core loop:

- **Enhancement, Transfer, and Sync were removed entirely** and replaced with a unified
  **Item Level** system.
- **Rare and Epic tiers were unified.**
- **Inheritance** moves item level between pieces, preserving traits and resonance.
  **Inheritance Stones** manage potential skills.
- **Armor Runes** are a new equipment layer, slotted through the Rune Book, granting boss
  damage, damage reduction, healing power and similar. Rune level caps at **60**.
- **Stat Conversion** lives at **Mafrion's Recombinator** (formerly the Skill Core device).
- Level cap **60**. Five skill specialization points at **56** and again at **60**. A Leveling
  Pass covers 56–60.

**Update 4.5.0 (2026-07-30)** added:

- **Archboss Ramux** and her dragon **Atirat** — two-phase fight in the frozen depths of
  Stillreach.
- New **Dimensional Trials** and **Guild Raids**.
- Archboss weapons at a **fixed item level of 85**. Ramux weapon recipes need **Thundercloud
  Scales** or **Skill Cores** as primary materials.
- Trait Resonance stat protection; reduced player-count requirements on some content.

Live event at time of writing: **Boosted Arch Bosses, 2026-07-28 → 2026-08-11**, four Arch
Bosses spawning simultaneously.

## Instructions this imposes on the suggesting AI

These belong in the system prompt, not just here:

1. **Patch-anchor everything.** Reason from 4.0.0+ mechanics only. Enhancement, Transfer and
   Sync no longer exist — never mention them. If a recommendation depends on pre-4.0 mechanics,
   it is wrong.
2. **Never invent an item name.** If identification didn't resolve an icon, say "unidentified
   item in slot 14", not a guess.
3. **Classify the currency before spending it.** Check the table above. Never propose spending
   a cosmetic or real-money currency on progression.
4. **State what you couldn't see.** If the currency bar was collapsed, say so and stop, rather
   than planning a budget around one visible number.
5. **Show the arithmetic.** "You have 179M Sollant, this costs 12M, leaving 167M" — advice
   about resources should be checkable at a glance.
6. **Price real money in dollars.** 1,000 Loyalty Points is $99.99. A boost ticket is $49.99.
   Say that, every time.

## Boss timers

Not a live feed. A **deterministic weekly schedule** per region (Americas / Europe / Asia) plus
the server's timezone and the 03:00 reset. Shipped as editable JSON and computed locally — no
runtime scraping, no dependency on a third-party site staying up. Needs a data refresh when a
patch changes the rotation.

## questlog.gg API

Public tRPC, undocumented, unversioned. Base
`https://questlog.gg/throne-and-liberty/api/trpc/`.

`characterBuilder.getCharacter?input={"slug":"<build-slug>","url":"<build-slug>"}` — **both
fields take the same build slug**, which is the *last* path segment of a build URL. Passing the
author's profile slug returns `NOT_FOUND`. The field name is a trap.

Cache hard, request rarely, always keep the manual-JSON-paste fallback working.
