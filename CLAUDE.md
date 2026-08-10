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

## The other rule — what the advice is actually for

**Optimise the best move available RIGHT NOW, not the ideal end state.** Stated by the product
owner, 2026-08-04:

> Players can't always acquire a gear set or a best-in-slot item — this takes an unprecedented
> amount of luck or real money. So we need to give the best advice for the player to do RIGHT NOW to
> progress and become stronger with what's available at the moment, with the least cost and path of
> resistance as possible.

questlog already publishes optimal builds. Restating "here is the ideal loadout" adds nothing and is
out of reach for most players. **The gap Loadstar fills is the next step**, and it only counts if it
is affordable and obtainable.

What this decides, whenever two recommendations compete:

- **Rank by gain ÷ (cost × difficulty)**, not by how much ground it closes toward best-in-slot. An
  upgrade the player can finish tonight beats a bigger one gated behind an archboss drop.
- **Aim at the nearest threshold, never the finished state.** A set at 1/4 pieces whose 2-piece
  bonus is one item away is cheap and real; "collect the other three" is a different, much larger
  ask. Same shape as the stat breakpoints and the Equipment watermark — find the closest cliff.
- **Free actions lead.** Stat redistribution costs nothing at all, which is why it outranks every
  upgrade that costs something.
- **Price the real cost.** Drop rate, expected kills, the weekly Flame cap, Lucent in dollars. An
  action that actually costs fifty hours or forty dollars must not read like a free reallocation.
- **Never withhold the expensive path** — state what it costs and let the player decide. Hiding it
  is as unhelpful as pushing it.

---

# Throne and Liberty

Verified against a live client on **2026-08-03**, patch **4.5.0**. Re-verify after any patch;
the item system in particular was rewritten wholesale in 4.0.0 and older guides are actively
misleading.

## Client languages — and the Russian client, which is a different game

**The game ships text in seven languages**: English, French, German, Korean, Japanese,
Spanish (LATAM) and Chinese (Traditional). Audio in three: English, Japanese, Korean. So a
screenshot may arrive in any of the seven, and every stat, currency and screen name in this file is
given in English as a *reference*, not as a description of what will be on screen.

**The player's language is frequently not the client's.** Ukrainian speakers, among others, run an
English client and ask questions in their own language. Loadstar's own interface therefore supports
languages the game does not — that mismatch is deliberate and should not be "corrected".

### The Russian client runs a much older version — treat it as out of scope

Confirmed 2026-08-04, and this is a correctness issue rather than a localisation one.

A Russian client exists at [throneandliberty.ru](https://throneandliberty.ru/), operated by
**ООО «АСТРУМ» (Astrum)** — a **different publisher from Amazon Games**, which is why its patch
cadence diverges. Per a player of the game it is still in the **T1 gear era**, far behind the global
build and **before the 4.0.0 item rewrite**.

**Essentially everything in this file is wrong for that client.** Item Level, Succession, Trait
Unlockstones, Trait Resonance, Redfrost and the 4.x currency set do not exist there — and
Enhancement, Transfer and Sync, which this file says never to mention, may still be live for them.

So the advice engine must **detect a Russian client and say plainly that its knowledge does not
apply**, rather than answering confidently about mechanics the player does not have. That behaviour
is in the system prompt. It is the one case where the right answer is "I can't help with this
version", and the failure mode it avoids is severe: every wrong answer would look completely
plausible.

The RU landing page carries no version or patch number, so **the actual RU patch level is
unconfirmed** — "T1 era" is a player report. If precision is ever needed, it has to come from the RU
client's own patch notes.

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

## Finding the game window — match the process, not the title

**Verified by an actual mis-capture, 2026-08-04.** Matching windows by title substring is unsafe,
and the failure is a privacy failure rather than a missing-window one.

Searching visible windows for `"THRONE AND LIBERTY"` matched **Firefox**, because the player had
a questlog build page open and the tab title contained the game's name. The tool was one step from
capturing a browser window and sending it to the AI provider. Anything the player has open — a
wiki, a Discord channel, a YouTube video about the game — collides the same way, and the more
engaged the player, the likelier the collision.

So window targeting is configuration, not a guess:

- **Process name is the primary key** (`TL.exe`). It does not collide with browser tabs.
- **Offer a picker over running windows**, so the player can point at the real thing once.
- **Offer selecting the game executable**, which yields the process name without them typing it.
- Title substring stays available as a fallback, but it must never silently win over a process
  match, and a browser process should never be captured on a title match alone.

The general rule: **the window Loadstar reads is the player's explicit choice, confirmed once,
not something inferred fresh on every capture.** Inference here is cheap to get wrong and the cost
of being wrong is sending the player's private screen to a third party.

The process name is **`TL`**, read off a live client on 2026-08-04 (build 1.443.22.7936). It ships
as the default in `ThroneAndLibertyModule.DefaultProcessName`, so a fresh install targets the right
window without the player configuring anything. This paragraph previously said the name was not
recorded, while a section further down had already recorded it — a file contradicting itself is
worse than a gap, because both halves read as authoritative.

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

**Tab 5 — Boss.** Boss-specific versions of the combat stats. Offence: Boss Melee/Ranged/Magic
Critical Hit Chance **−6** each, Boss Melee Hit Chance 12, Boss Ranged Hit Chance 12, Boss Magic
Hit Chance **−12**, Boss Melee/Ranged/Magic Heavy Attack Chance 7 each, Boss Bonus Damage 7.
Defence: Boss Melee/Ranged/Magic Endurance 12, Boss Evasion 8 across all three, Boss Heavy
Attack Evasion 0, Boss Damage Reduction 55.

**This is the most directly actionable tab seen so far**, because several values are *negative* —
they are active penalties against boss content, not merely unfilled headroom. A character
carrying −12 Boss Magic Hit Chance and −6 across all three Boss Critical Hit Chances is losing
damage in exactly the content that matters most (world bosses, archbosses, raids). Negative
boss stats should be surfaced ahead of positive-but-small gains elsewhere.

**Tab 6 — PvP ("Face Off").** PvP-specific mirrors of the same combat stats, all positive on
this character. Offence: PvP Melee/Ranged Critical Hit Chance 29, PvP Magic Critical Hit Chance
11, PvP Melee Hit 43, PvP Ranged Hit 50.2, PvP Magic Hit 43, PvP Heavy Attack Chance 24 across
all three, **PvP Damage +0%**. Defence: PvP Endurance 32 across all three, PvP Melee/Magic
Evasion 47.5, PvP Ranged Evasion 83.5, PvP Melee Heavy Attack Evasion 72.5, PvP Ranged/Magic
Heavy Attack Evasion 36.5, **PvP Damage Received −10%**.

**PvP and PvE are separate stat axes**, and this character is invested defensively in PvP
(−10% damage taken) with no offensive PvP damage at all. That is a coherent build choice, not a
gap — so the advice engine must not "fix" it. Which axis to optimise comes from the imported
build's own tags (questlog builds carry tags like *PVP Evasion*), not from assuming PvE.

**Tab 7 — Miscellaneous.** The economy tab, and the one most directly aligned with what
Loadstar exists to do. EXP Bonus +10%, Item Chance +3%, Abyssal Contract Token Bonus +0%,
Abyssal Contract Token Efficiency +7%, Weapon Mastery EXP Bonus +4.3%, Sollant Bonus,
Gathering Material Acquisition Rate +0% (several entries), Fishing Bonus Level 3, Fishing
Mastery Bonus **−3%**, Cooking Mastery Bonus +0%.

These are **acquisition multipliers** — rates at which Sollant, drops, tokens and XP come in.
That is the other half of the problem: the tool's remit is spending resources well, but earning
them faster compounds, and a player sitting at +0% Sollant Bonus and +0% gathering rate has
untouched leverage that no amount of clever spending advice replaces. Advice that only ever
optimises the spend side is solving half the problem.

Note Fishing Mastery Bonus is negative (−3%), so this tab carries penalties too.

All seven tabs now captured.

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

## Base stats are redistributable — the cheapest alignment action there is

The five base stats — **Strength, Dexterity, Wisdom, Perception, Fortitude** (observed 40 / 80 /
96 / 80 / 71) — are not fixed. The **total pool is accumulated from gear**, and the
**"Stat Change"** button at the bottom-left of the character panel redistributes that pool
freely across the five.

This changes the priority ordering more than anything else on this page. Every other
recommendation the tool makes costs something — Sollant, tokens, materials, time, or real money.
**Redistributing stats costs none of those.** If the imported questlog build specifies a stat
spread and the player's current spread differs, that is a zero-cost correction available right
now, and it should be surfaced *before* any upgrade that costs resources.

Practical rules for the advice engine:

- **Check the stat spread against the target build first**, every session. It is the highest
  return-on-effort action available and it is trivially reversible.
- **Never treat a stat spread as a sunk constraint.** Advice like "your Strength is low, so pick
  a Strength-scaling item" is wrong — the spread is an output the player controls, not an input
  they're stuck with.
- The pool grows as gear improves, so **new gear implies a re-check**, not just a new item.
- Confirm whether redistribution carries a cost or cooldown in 4.5.0 before promising it is free
  — the mechanic is confirmed, the price is not.

Stat Points shown as `0` on the panel are *unspent* points, distinct from this redistribution.

### Stat breakpoints — value is non-linear, and this is the headline optimisation

Hovering a stat reveals an **Achievement Effect** list: bonuses that unlock at specific
thresholds. Strength, observed at 40:

| Threshold | Effect |
| --- | --- |
| 30 ✓ | Max Health 750 |
| **40 ✓** | Damage Reduction 30 |
| 50 | Heavy Attack Chance 100 |
| 60 | Max Health 900 |
| 70 | Max Health 450 · Melee Defense 200 · Ranged Defense 200 |
| 80 | Max Health 450 · Heavy Attack Chance 60 |
| 100 | Max Health 600 · Damage Reduction 18 |
| 120 | Max Health 600 · Heavy Attack Damage 5% |

Achieved tiers render highlighted, unachieved ones greyed. The tooltip also breaks down the
stat's **sources** — Base 16, Equipment 23, Stellar Journey 1 — and offers `Alt` → View More.

**Stat value has two components, and conflating them produces wrong advice.**

1. **Continuous scaling.** Every point does something. Strength's own tooltip: *"Provides strong
   Defense in addition to increasing Max Health, Health Regen, Max Damage, and Min Damage."*
   That scaling is smooth — a point from 96 to 97 is not wasted.
2. **Discrete Achievement Effects.** The tiers above, layered *on top* of the continuous
   scaling. These are all-or-nothing: at 96 Wisdom you hold the 80-tier bonus and not the
   100-tier one, regardless of how close you are.

So breakpoints are a **bonus on top of a gradient**, not the only source of value. Do not
describe intermediate points as wasted — they aren't.

What this actually implies is subtler and more useful: **the marginal value of a point is
uneven.** A point that completes a threshold buys its continuous scaling *plus* a discrete
bonus; a point in the middle of a band buys only the scaling. So the optimisation is finding
**cheap threshold completions**, not reclaiming imaginary waste.

Worked example (Str 40, Dex 80, Wis 96, Per 80, For 71):

- Strength 40, Dexterity 80, Perception 80 sit exactly on tiers.
- **Wisdom 96 is 4 points from the 100 tier** — the cheapest available bonus on the sheet, and
  the first thing to look at.
- Fortitude 71 is 9 from 80; Strength 40 is 10 from 50.

Rules for the advice engine:

- **Report distance to the next threshold alongside the value**: "Wisdom 96 — 4 from the 100
  bonus" is actionable; "Wisdom 96" is not.
- **Rank candidate moves by points-required per bonus gained**, since every point also carries
  continuous value and nothing is truly stranded.
- **Never claim intermediate points are wasted.** They scale; saying otherwise is wrong and the
  player will know it.
- **Threshold positions are shared; rewards are not.** All stats captured so far use the same
  ladder — **30 / 40 / 50 / 60 / 70 / 80 / 100 / 120** — but each grants different bonuses. So
  distance-to-next-tier can be computed generically, while the *value* of reaching it must come
  from that stat's own tooltip.

### Captured ladders

Each tooltip also gives a source breakdown (Base / Equipment / Stellar Journey).

**Strength** — *"source of physical prowess; provides strong Defense in addition to increasing
Max Health, Health Regen, Max Damage, and Min Damage."* Sources at 40: Base 16 / Equipment 23 /
Stellar Journey 1.

| 30 | 40 | 50 | 60 | 70 | 80 | 100 | 120 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Max Health 750 | Damage Reduction 30 | Heavy Attack Chance 100 | Max Health 900 | Max Health 450 · Melee Def 200 · Ranged Def 200 | Max Health 450 · Heavy Attack Chance 60 | Max Health 600 · Damage Reduction 18 | Max Health 600 · Heavy Attack Damage 5% |

**Dexterity** — *"source of nimbleness; increases quickness and critical attacks in addition to
Evasion and Max Damage."* Sources at 80: Base 24 / Equipment 55 / Stellar Journey 1.

| 30 | 40 | 50 | 60 | 70 | 80 | 100 | 120 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Crit Hit Chance 100 | Bonus Damage 30 | Movement Speed 5% | Crit Hit Chance 120 | Crit Hit Chance 60 · Evasion 120 | Crit Hit Chance 60 · Bonus Damage 18 | Crit Hit Chance 60 · Attack Speed 4% | Crit Hit Chance 60 · Critical Damage 4% |

**Wisdom** — *"source of mental prowess; increases Max Mana and Mana Regen in addition to
cooldown abilities and Max Damage."* Sources at 96: Base 30 / Equipment 65 / Stellar Journey 1.

| 30 | 40 | 50 | 60 | 70 | 80 | 100 | 120 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Max Mana 750 | Debuff Duration −5% | Cooldown Speed 5% | Max Mana 900 | Max Mana 450 · Mana Regen 120 | Max Mana 450 · Cooldown Speed 3% | Max Mana 600 · Mana Cost Efficiency 3% | Max Mana 600 · Max Damage 10 |

**Perception** — *"source of insight and awareness; heightens awareness during battle, increases
the accuracy of attacks, CC effects, the duration of Buffs, and Max Damage and Min Damage."*
Sources at 80: Base 29 / Equipment 50 / Stellar Journey 1.

| 30 | 40 | 50 | 60 | 70 | 80 | 100 | 120 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Hit Chance 100 | Buff Duration 5% | Range 7.5% | Hit Chance 120 | Hit Chance 60 · CC Chance 100 | Hit Chance 60 · Buff Duration 3% | Hit Chance 60 · Range 5% | Hit Chance 60 · CC Chance 100 |

**Fortitude** — *"the most fundamental status that affects Endurance, CC Resistances, Magic
Defense, Max Damage, and Min Damage."* Sources at 71: Base **10** / Equipment 60 / Stellar
Journey 1.

| 30 | 40 | 50 | 60 | 70 | 80 | 100 | 120 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Endurance 100 | Magic Defense 200 | Heavy Attack Evasion 100 | Endurance 120 | Endurance 60 · CC Resistances 100 | Endurance 60 · Heavy Attack Evasion 60 | Endurance 60 · Critical Damage Resistance 4% | Endurance 60 · Heavy Attack Damage Resistance 5% |

Expanded contribution at 71: Main Min/Max Damage **+49** each, Magic Defense **505**,
Melee/Ranged/Magic Endurance **585** each, and all eight CC Resistances at **405**.

All five stats captured.

### Base values decide cost, and they differ sharply

The source breakdown is the most important number on each tooltip, because the 2× escalation
triggers on **base**, not the displayed total:

| Stat | Displayed | Base | Equipment | Cost band |
| --- | --- | --- | --- | --- |
| Strength | 40 | 16 | 23 | 1× — 14 points before escalation |
| Dexterity | 80 | 24 | 55 | 1× — 6 points before escalation |
| Wisdom | 96 | **30** | 65 | **2× already** |
| Perception | 80 | 29 | 50 | 1× — 1 point before escalation |
| Fortitude | 71 | **10** | 60 | **1× — 20 points before escalation** |

**Fortitude is the cheapest stat on this character by a wide margin**, and nothing about the
displayed value (71, the lowest) reveals why — it is cheap because its base is 10 while
everything else has been pushed to the escalation threshold. Its equipment contribution of 60 is
doing the heavy lifting.

Reworking the earlier comparison with real costs:

- **Fortitude 71 → 80**: 9 points at 1× = **9 stat points**, for Endurance 60 + Heavy Attack
  Evasion 60.
- **Wisdom 96 → 100**: 4 points at 2× = **8 stat points**, for Max Mana 600 + Mana Cost
  Efficiency 3%.

Comparable cost, and both are far better than Dexterity or Perception, which need ~20 levels to
reach their next tier. That ranking is invisible if you look only at displayed values or only at
distance-to-threshold — which is exactly why the advice engine must read the base breakdown.

### `Alt` → View More exposes the continuous contribution

Holding `Alt` on a stat tooltip expands it to show what the stat currently contributes through
continuous scaling — separate from the discrete tiers. Perception at 80:

Main Min Damage **+56**, Main Max Damage **+56**, Melee/Ranged/Magic Hit Chance **740** each,
Weaken/Stun/Petrification/Sleep/Silence/Bind/Fear/Collision Chance **500** each, Buff Duration
**+43%**.

This is the proof of the two-component model, and it is **directly usable**: the tool can
quantify exactly what a stat is delivering right now, rather than inferring it. Capture the
expanded view, not the collapsed one.

Captured expansions:

| Stat | Value | Continuous contribution |
| --- | --- | --- |
| Strength | 40 | Main Min/Max Damage +24 · Max Health 2,100 · Health Regen 127 · Melee Defense 150 · Ranged Defense 150 |
| Perception | 80 | Main Min/Max Damage +56 · Melee/Ranged/Magic Hit Chance 740 · all eight CC Chances 500 · Buff Duration +43% |
| Fortitude | 71 | Main Min/Max Damage +49 · Magic Defense 505 · Melee/Ranged/Magic Endurance 585 · all eight CC Resistances 405 |
| Dexterity | 80 | Main **Max** Damage +95 · Attack Speed +21% · Melee/Ranged/Magic Evasion 280 · Melee/Ranged/Magic Critical Hit Chance 740 |

| Wisdom | 96 | Main **Max** Damage +117 · Max Mana 6,420 · Mana Regen 732 · Cooldown Speed +29.5% |

All five expansions captured.

**Damage contribution falls into two families, not one curve and not five.** The first pass
looked like a single formula, then Dexterity broke it; with all five captured the actual
structure is clear:

| Family | Stats | Per point above 10 | Applies to |
| --- | --- | --- | --- |
| Balanced | Strength, Perception, Fortitude | **×0.8** | **both** Min and Max Damage |
| Max-only | Dexterity, Wisdom | **×≈1.36** | Max Damage only |

Every observation fits: Strength 40 → 24, Fortitude 71 → 49, Perception 80 → 56; Dexterity
80 → 95, Wisdom 96 → 117.

So the max-only stats buy roughly **1.7× more Max Damage per point** but contribute no Min
Damage — they raise the ceiling and widen the damage range, while the balanced stats lift the
whole band. That is a real build-shaping trade-off (burst and variance versus consistency), and
it is invisible from the breakpoint tables alone.

**Verify before relying on the constants.** Five points across two families is a good fit, not a
proof, and 1.36 is an estimate from two samples. The *family split* is solid — it is stated
outright in the tooltips. The multipliers are inference.

**The tooltip's one-line descriptor predicts this exactly**, and is worth parsing rather than
skipping:

| Stat | Descriptor says | Damage effect |
| --- | --- | --- |
| Strength | "Max Damage, and Min Damage" | both |
| Perception | "Max Damage and Min Damage" | both |
| Fortitude | "Max Damage, and Min Damage" | both |
| Dexterity | "Evasion and **Max Damage**" | max only |
| Wisdom | "cooldown abilities and **Max Damage**" | max only (predicted, unconfirmed) |

So the descriptor tells you *which* stats a point feeds, and the expanded view tells you *how
much*. Both are needed; neither substitutes for the other, and no formula substitutes for
either.

### Escalating point cost — this inverts naive "distance to next tier" advice

**Raising a stat gets more expensive as it climbs**, in three bands — and all three are now
**exact**, decompiled from questlog's own allocation transform rather than inferred:

```js
allocated <= 20        -> contributes allocated       (marginal 1.00 -> 1x cost)
20 < allocated <= 40   -> 20 + (a-20)*0.5             (marginal 0.50 -> 2x cost)
allocated > 40         -> 30 + (a-40)*0.25            (marginal 0.25 -> 4x cost)
```

Since `base = 10 + allocated`, the bands land on **base 30** and **base 50**:

| Base | Cost per further point |
| --- | --- |
| 10–29 | **1** |
| 30–49 | **2** |
| 50+ | **4** |

This **confirms the 2x threshold at base 30** that was already recorded from tooltips, and
**resolves the 4x escalation that this file previously flagged as reported-but-unverified**: it
triggers at **base 50**. Two independent derivations agreeing on the 30 boundary is a good sign the
model is right.

`StatPlanner.PointsToRaise` implements all three. Before that it modelled only two, so a base-50
stat was priced at half its real cost — meaning any recommendation touching a heavily-invested stat
came out looking cheaper than it was.

This changes the arithmetic completely, and it is why raw distance-to-tier is the wrong metric:

| Stat | Value | Base | Distance to next tier | Actual cost at 2× |
| --- | --- | --- | --- | --- |
| Wisdom | 96 | **30** | 4 | **~8 points** |
| Perception | 80 | 29 | 20 | ~40 points |
| Dexterity | 80 | 24 | 20 | ~40 points |
| Strength | 40 | 16 | 10 | ~10–20 points |

Every base value on the observed character sits at or near the 30 threshold, so **most further
investment is already in the doubled band**.

Corrections this forces on the advice engine:

- **Cost must be computed from the base component, not the displayed total.** The tooltip's
  source breakdown (Base / Equipment / Stellar Journey) is what makes this possible — a stat
  showing 96 with base 30 is in the expensive band, while one showing 96 with base 12 is not.
- **Rank by bonus-per-stat-point-spent, not by distance to the threshold.** My earlier framing —
  "Wisdom 96 is four points from 100, the cheapest tier on the sheet" — was wrong: it is
  roughly eight stat points, and the comparison against other stats shifts accordingly.
- **Equipment-sourced stat points bypass the escalation entirely**, which makes gear that
  grants a lagging stat disproportionately valuable versus spending points on it.
- Combine with redistribution: crossing a threshold costs only reallocation, which still makes
  it among the highest-confidence recommendations available.

## Compare totals, not allocated points — and always show what a move costs

**The rule here came from a recommendation that was correct but incompletely justified.** Worth
stating precisely, because the distinction changes what a future session should do.

The advice — *move allocated points out of Strength into Dexterity and Perception* — was
**right**. PvE healers do prefer Dexterity, Perception and Wisdom over Strength; that is the
meta and it is confirmed by the player. What was missing was the **accounting**: the move drops
Strength from 40 to 34 and gives up the Strength 40 tier's Damage Reduction 30, and that was
never surfaced.

So the failure was not the conclusion, it was presenting a trade as a pure gain. **Do not let
this discourage making specific recommendations** — specificity is the product. Make them, and
state the cost alongside.

questlog stores each build's `attributes` as **allocated** points (`str`, `dex`, `int`, `per`,
`con` — mapping to Strength, Dexterity, **Wisdom**, Perception, **Fortitude**). Base starts at 10
per stat, so allocated = base − 10. Verified: the reference character's bases (16/24/30/29/10)
give 59 allocated, exactly matching five of the six target loadouts' totals.

The trap is that **allocated targets assume the build author's equipment.** A build specifying
`str: 0` is not saying "have no Strength" — it is saying "allocate none, because my gear supplies
what I need." Copy that number onto a character with different gear and you get a different
total, and possibly lose a breakpoint the author never lost.

Worked example — the recommendation is sound, the accounting is what's easy to miss:

- Target build says `str: 0`; the player has `str: 6` allocated.
- "Move 6 out of Strength" is **the right call for a PvE healer** — Dexterity, Perception and
  Wisdom outperform Strength for that role.
- But the player's Strength is 40 = base 16 + **equipment 23** + Stellar Journey 1.
- Dropping allocation takes base to 10 and **total to 34** — still above the 30 tier, but it
  **gives up the Strength 40 tier and its Damage Reduction 30**.
- The move is still worth making. The player just needs to be told it costs that.

The correct procedure:

1. Convert the target's allocated points to a **projected total** using *this character's*
   equipment and Stellar Journey contributions, not the author's.
2. Check which **breakpoints are held or lost** at that projected total, for every stat touched.
3. Price the move in stat points using the **base-driven escalation** — refunds and spends are
   not symmetric when a base sits at the 30 threshold.
4. **Present gains and losses together.** "Move 6 out of Strength into Dexterity — gains the
   Dexterity 30-base tier, costs Damage Reduction 30 from Strength 40" is complete. "Move 6
   points out of Strength" is the same advice with the price hidden, and a player who later
   notices the loss will stop trusting the rest.

### Resolved: Strength versus Dexterity/Perception/Wisdom for PvE healers

**Dexterity, Perception and Wisdom win.** Published guidance puts Wisdom (mana, mana regen) and
Perception (buff duration, debuff chance) first for healers, at minimum 30 each, with Strength
as the mitigation stat — and the player confirms PvE healers prefer those over Strength in
practice. So on a healer, giving up Damage Reduction 30 to fund Dexterity and Perception is the
correct trade.

Recommend it. Just quote the cost.

This is a role-specific answer, not a universal one — the same move on a tank or bruiser would
be wrong. Read the build's tags (`pve`, `healer`, `pvp`) before applying it.

## Artifacts — six slots, matched sets, and a link to the boss timer

Researched 2026-08-04 and **cross-checked against the equipment catalogue, which matches exactly**.
Six slots: **four minor Talistone slots, one active Solarstone, one passive Lunarstone**. The
catalogue's `equipmentType` values confirm it precisely:

| Type | Count | Example |
| --- | --- | --- |
| `talistone1` … `talistone4` | 14 each | Plains Ravager Talistone I–IV |
| `gemstone1` | 14 | Plains Ravager **Solarstone** (active slot) |
| `gemstone2` | 14 | Plains Ravager **Lunarstone** (passive slot) |
| `boonstone` | 36 | Syleus's Abyss 2F Abysstone |
| `riftstone` | 13 | **Adentus** Riftstone |
| `stellarite` | 2 | Quality Stellarite |
| `brooch` | 14 | Brooch of Certainty |

Two things fall out of this that matter for advice:

**Artifacts come in matched sets.** All six "Plains Ravager" pieces share a prefix, and there are 14
of each type — so there are roughly 14 complete artifact sets, and mixing across sets is a decision
with a cost. Set identity is readable straight from the name prefix.

**Riftstones are named after field bosses.** "Adentus Riftstone" — and Adentus is one of the seven
field bosses in `boss-schedule.json`. That is a direct join between the boss timer feature and gear
progression: knowing tonight's boss tells you which riftstone is farmable tonight. Wire the two
together; it turns a countdown into a reason to log in.

Sources: specific Abyss Dungeons in Talandre drop artifact types in a chest, Nebula Island creatures
drop them, and group dungeons reward them. Note artifacts read **380/380 — maxed** on the reference
character's Combat Power tooltip, so for that player this is a finished system and any advice to
push it is wasted effort.

## Weapon Mastery — 353 headroom, and the thresholds are cross-weapon

The second-largest gap on the reference character after runes and accessories (1047/1400). Masteries
grant real buffs and special stats, not flavour, so this deserves the attention its headroom implies.

> **Documented exception to the patch-anchoring rule.** Pre-4.0.0 material is untrustworthy for
> *items*, and this file says so repeatedly. **Weapon Mastery is different**: per a player of the
> game, the system is largely unchanged and the only revision was raising the point ceiling. So older
> mastery guides are usable here. Do not discard them the way you must discard pre-4.0 item guides.

- **Maximum is 220 points per weapon** (player-reported). Guides still say 200, which was the figure
  before the ceiling was raised — treat 200 as stale rather than as a contradiction.
- Each weapon tree has **three branches**, each aimed at a playstyle (the Dagger tree is Disguise,
  Poison, Assassination).
- **Branches are linear.** To reach the passive at the end of a branch you must unlock everything
  before it. So an end-of-branch passive is never a cheap pick, and advice naming one must price the
  whole path to it, not just the node.

### The slot thresholds are totals across all weapons, not per weapon

**12 Mastery Skills**, unlocking two at a time at **130, 260, 390, 520, 650 and 780** points, with
**four slottable at once** and the four slots unlocking at **130 / 260 / 390 / 520**.

Those numbers only make sense as a **cross-weapon total**: 780 is far beyond any single weapon's
220 ceiling, so the pool being measured is the sum of every weapon's mastery. That inverts a natural
assumption and produces genuinely different advice:

- **Levelling a secondary weapon is not wasted progress** — it feeds the same shared threshold that
  unlocks slots and skills.
- And the XP economics support it: a **secondary weapon earns 50% of the XP** the active weapon
  receives, passively, while you play. So the off-weapon accrues toward shared thresholds at half
  rate for no extra effort.

This is the same discrete-breakpoint shape as the stat ladder, so report **distance to the next
threshold**: "you are 40 points from your third mastery slot" is advice; "raise weapon mastery" is not.

### Costs and examples

Unlocking passives costs Sollant, and **deactivating costs 10,000 Sollant per deactivation** — so
respeccing has a real, repeatable price that advice should quote before recommending a rearrangement.

Mastery skills give substantial effects, e.g. **"Overcome Crisis"** — Attack Speed +20% and Cooldown
Speed +35% for 5s.

> **Corrected 2026-08-04.** An earlier draft of this section also listed a mastery skill called
> **"Potential"** granting +1 to all five base stats. **There is no such mastery skill** — confirmed
> by a player of the game. It came from misreading a guide page where "Potential" refers to the
> separate Potential/latent-ability system, not to a mastery node. Removed rather than left in with
> a caveat, because a plausible invented skill name is exactly the failure mode this file warns
> about everywhere else.

Tab 7's **Weapon Mastery EXP Bonus +4.3%** feeds this system directly — an acquisition multiplier on
the very axis with 353 headroom, which is a good example of why the economy tab is not a footnote.

> **220 is confirmed** by a player of the game as the current single-weapon cap — but note the
> wording, "at the moment": the ceiling has already moved once (200 → 220) and the system is
> otherwise stable, so **the cap is the field most likely to change on a future patch**. Treat it as
> a value to re-check after every update rather than a constant. `WeaponMastery.MaxPointsPerWeapon`
> is the single place to change it, and `MinimumWeaponsFor` derives from it, so the "you need a
> fourth weapon for 780" conclusion updates automatically.
>
> Still to confirm: node counts per branch.

## Stellar Journey — the third component in every stat tooltip

Added in **3.34.0**. This is the "Stellar Journey 1" line that appears in every stat's source
breakdown alongside Base and Equipment.

- Grants **permanent** stat increases through **Starry Memory**, which is consumed automatically on
  acquisition rather than spent by the player.
- Sources: **Adventure Codex**, **Exploration Codex**, and **Traces of Spacetime**, plus **Rift
  Rebellions** and **Relic Fishing** locations.
- At 100% Traces of Spacetime completion, **2 random Rift Rebellions and 1 Relic Fishing location**
  appear daily across all dominions, each granting a Special Resistance Medal.
- Menu: Character → Stellar Journey. Unlocked by Adventure Codex → Prologue → The Star-Born.

**Consequence for the stat planner:** Stellar Journey contributes to a stat's displayed total but is
**not** part of the redistributable allocated pool — it behaves like equipment. `StatPlanner`
already handles this correctly by treating everything above base as one external contribution held
constant across a redistribution. Do not "reallocate" it.

Combat Power showed 462/524 here — 62 headroom, the smallest non-zero gap, so it is a low priority
for a character at this stage despite being permanent.

## Contracts and Abyssal Contract Tokens

- **Resistance Contracts** are the main source of Abyssal Contract Tokens.
- **10 contracts per day**, each granting **50 tokens** — so the daily ceiling is a known 500.
- Entry is free, but opening the final treasure chest costs Dimensional Contract Token Points.

A hard daily cap makes this schedulable rather than grindable, which is exactly the kind of thing
advice should say plainly: there is no way to go faster, only a way to not miss days. Tab 7's
**Abyssal Contract Token Bonus** and **Token Efficiency +7%** modify this stream.

## Classes are weapon pairs — all 45, captured from questlog's own filter

There is no class system. A character equips **two weapons** and the pair has a name. Ten weapons
pair 45 ways and **all 45 are named classes** — `C(10,2) = 45`, no gaps.

Captured 2026-08-06 from questlog's class filter by reading the URL slug each option produces
(`?class=gauntlet-sword`), and held in `TlClasses`. **Not taken from community guides**: published
lists still show 21 or 28 classes because they predate Spear, Orb or Gauntlets, and Orb and Gauntlet
between them account for **17 of the 45**. Gauntlets shipped 2026-06-25 with Nix, so guide coverage
of them is close to nil.

Verified three ways: 45 names to 45 distinct pairs with none missing or repeated; every weapon
appearing in exactly 9 classes; and the five the product owner named independently — Oracle, Seeker,
Gladiator, Ravager, Bulwark — all matching.

| Weapon | Its nine classes |
| --- | --- |
| `bow` Longbow | Impaler · Infiltrator · Liberator · Ranger · Scout · Scryer · Seeker · Strider · Warden |
| `crossbow` | Battleweaver · Cavalier · Crucifix · Fury · Marauder · Outrider · Raider · Scorpion · Scout |
| `dagger` | Berserker · Brawler · Darkblighter · Infiltrator · Lunarch · Ravager · Scorpion · Shadowdancer · Spellblade |
| `gauntlet` | Bastion · Brawler · Bulwark · Channeler · Juggernaut · Marauder · Mystic · Skirmisher · Strider |
| `orb` | Bulwark · Crucifix · Enigma · Guardian · Justicar · Lunarch · Oracle · Polaris · Scryer |
| `spear` | Cavalier · Eradicator · Gladiator · Impaler · Polaris · Shadowdancer · Skirmisher · Steelheart · Voidlance |
| `staff` | Battleweaver · Channeler · Disciple · Enigma · Eradicator · Invocator · Liberator · Sentinel · Spellblade |
| `sword` Sword and Shield | Bastion · Berserker · Crusader · Disciple · Guardian · Raider · Steelheart · Templar · Warden |
| `sword2h` Greatsword | Crusader · Gladiator · Juggernaut · Justicar · Outrider · Paladin · Ranger · Ravager · Sentinel |
| `wand` Wand and Tome | Darkblighter · Fury · Invocator · Mystic · Oracle · Paladin · Seeker · Templar · Voidlance |

**`sword` is Sword and Shield and `sword2h` is the Greatsword.** Confusing those two names a
different class, and both appear in nine classes each, so there is no way to recover from it later.

### This is what lets Loadstar stop demanding a build URL

Two weapons name a class, and `searchCharacters` filters on **`mainHandWeapon` and `offHandWeapon`**
with `sort=likes-month`. So the app can read the player's weapons off the character sheet, identify
the class, and offer the builds people are actually liking *now* — instead of requiring them to go
and find a questlog URL before it will say anything.

Note `likes-month` rather than lifetime likes: a build with 200 lifetime likes and none this month
was written for a patch that no longer exists. And note which filter names are real —
`mainHandWeapon`/`offHandWeapon` filter, while `weaponTypes`, `weapons`, `class` and `sortBy` are
**accepted and silently ignored**, returning the unfiltered top of the list. An ignored filter looks
exactly like a successful query, so results are re-checked against the requested pair.

### Weapon detection must be right, not plausible

A wrong pair names a different class, and every recommendation afterwards is confidently aimed at a
character the player is not playing. Nothing downstream contradicts it and the player cannot tell.
This is the read with the worst failure mode in the product.

The model is reliable at **text** and unreliable at **naming icons** — the rest of this file says so
repeatedly, and the boss-schedule capture proved it when a badge a person could see plainly turned
out to be three pixels after downsampling. Weapon slots on the character sheet are icons. So:

- **Prefer text sources**, in order: a weapon **tooltip** (states the type outright), the **Weapon
  Mastery** screen, the **skills** screen. The model reports which it used in `weaponsSource`.
- **An icon read is stored unconfirmed** and must be seen twice, on separate captures, agreeing.
- **The player's own confirmation always wins** and is never overwritten by an icon read.
- Corroborate with numbers when only icons are available: the expanded sheet's **Range** (~30m means
  ranged, and a melee weapon cannot be) and **Attack Speed** separate weapon families without any
  icon recognition at all.

## PvP and PvE are different builds — measured, not assumed

The tool must know which axis the player is on before it gives any gear or trait advice, and the
difference is real enough to measure. Mined 2026-08-04 from `characterBuilder.searchCharacters`,
**five pages each of `pvp`- and `pve`-tagged builds, 180 weapon slots per side**. Trait counts:

| Weapon trait | PvP | PvE | Reading |
| --- | --- | --- | --- |
| `all_double_attack` | 131 | 162 | Universal — carries no signal |
| `all_accuracy` | **117** | 69 | **PvP marker** |
| `all_critical_attack` | 85 | **119** | **PvE marker** |
| `attack_speed_modifier` | 84 | 93 | Universal |

| Heroic trait | PvP | PvE | Reading |
| --- | --- | --- | --- |
| `per` (Perception) | 38 | 46 | Both — the dominant heroic pick either way |
| `all_accuracy` | **25** | 13 | **PvP marker** |
| `con` (Fortitude) | **21** | 10 | **PvP marker** |
| `dex` (Dexterity) | — | **25** | **PvE marker** |
| `int` (Wisdom) | 12 | 20 | Leans PvE |

**The clean inversion is accuracy versus critical.** PvP builds buy `all_accuracy`, PvE builds buy
`all_critical_attack`, at roughly 1.7x each way. The mechanism is straightforward: player targets
stack Evasion and mobs do not, so hit chance is contested in PvP and largely solved in PvE, which
frees PvE builds to spend on crit.

On the heroic side PvP leans **Fortitude** (Endurance, CC resistance) while PvE leans **Dexterity**
and **Wisdom** — matching the account from a player of the game: PvP gear favours
Endurance / Heavy Attack Resistance or Evasion, PvE favours Evasion with Defense / Damage Reduction.

**Two defensive archetypes exist and must not be mixed: Endurance-stacking and Evasion-stacking.**
Community guidance on heroic traits says to take Heavy Attack Evasion and Critical Damage
Resistance, then pick *either* Endurance *or* Evasion "depending on which defensive stat you are
stacking" — so the third pick is only correct relative to a chosen archetype. Advice that
recommends Endurance to an Evasion build is actively harmful, and it is the kind of error that
looks reasonable in isolation.

**Caveats on this data.** `searchCharacters` inlines only main-hand and off-hand, so these are
*weapon* traits; the armour side, where the Endurance/Evasion split lives most clearly, needs
per-build `getCharacter` calls to sample. Tags are author-supplied and unmoderated. And the sample
is popularity-ordered, so it reflects what is fashionable as much as what is optimal.

**How the app should decide the axis**, in priority order:
1. The imported build's own **tags** (`pvp`, `pve`, `healer`, `tank`, `siege`, `dps`) — authoritative.
2. Failing that, the **trait fingerprint above** — accuracy-heavy reads PvP, crit-heavy reads PvE.
3. Failing both, **ask**. Never default to PvE silently; the character sheet's separate PvP tab
   showed a coherent defensive PvP investment that a PvE assumption would have "fixed".

## Traits, Heroic gear, Potential and Skill Cores — the 4.0.0 equipment overhaul in full

Researched 2026-08-04 from the official 4.0.0 equipment-overhaul notes. This section supersedes
several earlier summaries in this file; where they disagree, this is the newer reading.

### Gear now arrives with NO traits, and only Heroic+ can have them

**"When you obtain equipment, it has no traits."** Traits are unlocked, not rolled. And the gate is
rarity:

> Only attributes on equipment items of **Heroic rarity and above** can be unlocked and levelled up.
> Rare and lower rarities have traits removed.

This reframes the `Locked` entries seen on a live tooltip: they are **unclaimed trait slots awaiting
an Unlock Stone**, and they exist only on Heroic and above. It also means **rarity is a hard gate on
the entire trait system**, so "upgrade this Rare piece's traits" is not a thing that can be done.

- **3 traits per piece**, unlocked with **Trait Unlock Stones** (Unique-grade gear needs
  **Unique Trait Unlock Stones**).
- Traits grow with **Trait Enhancement Stones**, which carry **their own item level** and
  "can be used to enhance the attributes of equipment of the **same or lower** level". So enhancement
  stones are not fungible — holding low-level stones does not help a high-level piece, and that
  mismatch is a real, checkable blocker worth surfacing.
- Levelling consumes the **experience points contained in the stone**.
- **Unique traits** on Unique-grade gear level with **trait-specific stones**, not generic ones.

### Trait Resonance — four slots, max 10 each, 40 for the cap

> Unlocking Resonance activates **four slots**. Each slot can be upgraded to a maximum of **10**, and
> to reach the maximum resonance level (**Level 4**), the total sum of the slot numbers must be **40**.

Uses **Attribute Resonance Stones** to unlock and to level individual slots. Because the bracket is
driven by the *sum*, resonance has the same "lagging component decides progress" shape as the
Equipment watermark — pushing one slot to 10 while another sits at 2 does nothing for the bracket.

### Potential / Latent Abilities — and the capture opportunity

- Randomly applied to equipment **on acquisition**.
- They "enhance skills or increase a character's stats", and the skills within them can themselves
  be levelled.
- **The probability is visible in the tooltip of an unacquired item.** That makes it another
  user-initiated capture worth prompting for, and it is exactly the sort of number the model reads
  reliably.
- **They survive sealing** — "even if equipment with unlocked latent abilities is sealed, the latent
  abilities remain", which matters directly for Redfrost items.

**Purification is one of the moments a Potential can manifest.** Per a player of the game: purifying
a Redfrost item carries a small chance of the result also bearing a Potential ability, and the three
observed kinds are **an enhancement to a weapon mastery**, **+1 level to a skill**, or **a random
stat such as Max Health**.

Two consequences, and the first is the one that changes arithmetic already written here:

1. **A purify's item outcome is not a fixed prize.** `PurifyChainEstimator` treats "you got the item"
   as a single terminal value, which is the right shape for planning *how many kills*, but it
   understates the spread — two players who both "got the item" can hold very different results.
   Advice should describe the Potential as upside on top, never fold it into an expected value as if
   it were guaranteed.
2. **Potentials are a cross-system link.** A Potential that enhances a weapon mastery or raises a
   skill feeds two of the systems with real Combat Power headroom (Masteries 353, Skills 112) from an
   *equipment* action. So purification is not purely a gear activity, and advice that files it under
   "gear" alone undersells it.

Note this also interacts with the Skill Core rule above: using Skill Cores **as materials** yields a
result with **no** Potential abilities. So the two are in direct tension — the material route
forfeits exactly this lottery, and a player should be told that before they melt something.

**Potentials survive being sealed and traded, so the auction house sells them.** An item a player
sealed while it carried Potential abilities still carries them when someone buys it. That turns the
auction house into the one **deterministic** route to a specific Potential: instead of purifying and
hoping, you buy the roll you want, already known.

This is a genuinely important branch for advice, and it is also a trap:

- **The auction house trades in Lucent, and Lucent is real money.** So "just buy one with the
  Potential you need" is a *purchase*, not a progression step. Per the currency rules above it must
  be priced in dollars and presented as optional, never slipped in beside earned-currency actions as
  though they were comparable.
- It is nonetheless often the *correct* answer to state, because the alternative is an unbounded
  random chain. Say both: what the gamble costs in time, what the certainty costs in money, and let
  the player choose. Withholding the paid option is as unhelpful as pushing it.
- `auctionHouse.getAuctionItem` gives per-region price history, so the money side of that comparison
  is real data rather than a guess — use it, and note `regionId` must follow the player's server.

### Skill Cores

- **60 new Hero Skill Cores**, equipped on **Unique armour and accessories** (brooches excluded).
- Equipment skills were **removed** from unique armour and accessories and replaced by skill-core
  slots — so a pre-4.0 guide describing armour skills is describing something that no longer exists.
- **Melting economics:** melting gives **8 Resin Cores**; **60 Resin Cores craft a Resin Flower**,
  which yields **a chance at one of 60 Skill Cores**. Weapon cores give no resin.
- **Tradable skill cores become untradable once equipped** — a one-way door worth warning about
  before a player equips something they might have wanted to sell.
- Using Skill Cores **as materials** produces a result that receives **no Potential abilities**.
  A genuine trade-off, and easy to regret silently.

### Succession (Inheritance) — how gear levels move, in full

The 4.0.0 name is **Succession**, though the UI and community both still say Inheritance. Reached
via the **Inheritance button at the bottom of the bag**.

**What it does.** Transfer the item level of a piece with a **higher** item level into the piece you
are actually using, levelling that piece up. The direction is fixed: level flows from the higher
source into the lower target, never the reverse.

**Why it is the core gearing loop, not a side feature.** The target **keeps its own growth** — its
traits and resonances survive. So a drop with a great item level but no invested traits becomes fuel
for the piece you have already sunk Trait Unlock Stones, Enhancement Stones and Resonance Stones
into. Without Succession, a high-level drop would mean re-doing all of that investment; with it,
level and investment are separable. That is the single most important thing to understand about
gearing after 4.0.0.

**Two things can be transferred**: the **level**, and the **latent abilities** (Potentials).
Transferring Potentials is what consumes an **Inheritance / Succession Stone**.

**Where Succession Stones come from:**
- Purchased from the **Resistance Supplies Merchant**.
- **Low probability from melting down equipment of level 51 or higher** — which gives high-level
  junk a use, and is worth saying before a player vendors it.

**The inheritance counter, and the correction.** Each transfer of a latent ability **reduces a
count**, and at **0 it can no longer be inherited by any other equipment**. An earlier version of
this file called that irreversible. **It is not** — *"if you use resources to seal an item, the
number of times its latent ability can be inherited will be reset."*

So a maxed-out counter is a **cost, not a wall**: reseal the item (Seal Key + Sollant) and the
counter comes back. Advice should say exactly that rather than declaring the item finished, because
"this Potential can never move again" would steer a player into abandoning something recoverable.

**This gives sealing a third purpose.** It makes loot tradable, it carries Potentials through a
trade, *and* it resets the inheritance counter. A Seal Key is therefore not just a merchant good —
it is a progression consumable, and advice about Seal Key supply should account for all three uses.

**The ceiling.** "The level range that can be achieved through inheritance extends up to the
equipment's maximum level" — so Succession cannot push a piece past its own cap, and the cap comes
from the catalogue's `itemStats` levels (0–85 observed, with 85 reserved for Archboss weapons).

Item level progression remains "the average of the highest levels within each category: weapons,
armour and accessories" — the same rule as the Equipment watermark, stated from the other direction.
Which means Succession interacts with the watermark exactly as the watermark section warns:
succeeding a piece in your already-strongest category raises power and moves the watermark by
nothing.

## Runes — the largest Combat Power gap, and the most misdocumented system

The Combat Power tooltip put Runes at **542 headroom**, the biggest single gap on the reference
character, so this is the highest-value system to get right. Verified 2026-08-04 against
`characterBuilder.getEquipmentRunes` and `getRuneSynergies` (live 4.5.0). **Published guides are
wrong about the level cap** — check API data before quoting any of them.

### Correction: rune levels go to 120, not 60

An earlier note here said "Rune level caps at 60", and community guides say grey 20 / green 40 /
blue 60. Both are outdated. The live cap depends on grade:

| Grade | Max level | Runes |
| --- | --- | --- |
| 11 | **20** | 18 |
| 21 | **40** | 18 |
| 31 | **60** | 36 |
| 41 | **90** | 18 |
| 42 | **120** | 18 |

Internal corroboration: the reference build's own payload carries `"runes": {"0": {"lvl": 120 …}}`.
A tool that caps its reasoning at 60 would understate rune headroom by half on an endgame character.

### A rune's stat is a weighted random roll, not a choice

This is the single most important mechanic here and no guide states it plainly. Each rune carries a
`random_stat_group_1` array — a pool of possible stats, each with `stat_id`, `base_value`,
`max_level`, an explicit `levels` string giving the exact value at every level, and a
**`probability`**. The probabilities **sum to exactly 100**.

So "slot an Attack Rune for critical hit" is not a plan. On a common weapon attack rune, each of
`melee/range/magic_critical_attack` is **8.4%**, the accuracy and double-attack stats are 6.3% each,
and `buff_given_duration_modifier` / `skill_power_amplification` are 9.05% each. Advice must talk
about **expected outcomes and rerolls**, not about picking a stat.

The `levels` string makes the payoff exactly computable — e.g. `"5 5 10 15 20 … 100"` means a
level-20 roll of that stat is worth 100. Never estimate rune value; look it up.

### Four rune types, not three — and Chaos is a wildcard

`runeType` is **attack (36), defense (36), assist (36), chaos (24)**. Guides describing "three
archetypes" predate Chaos.

**A Chaos rune counts as any type for synergy purposes.** Confirmed by the data: its id is
`Weapon_**All**_Rune_Usable_kA_001`, and its stat pool draws from all three archetypes at once —
44 possible stats spanning attack (`*_critical_attack`, `*_double_attack`, `*_accuracy`,
`skill_power_amplification`), defence (`*_evasion`, `*_critical_defense`, `*_double_defense`,
`skill_power_resistance`) and support (`hp_max`, `hp_regen`, `cost_max`, `cost_regen`,
`heal_modifier`, `shield_modifier`, and all eight CC accuracies).

That is why builds reach for them: a Chaos rune **completes an ordered synergy from any position**
while letting the player put the stat budget where they actually want it, dropping the stats the
archetype would otherwise have forced on them.

**They do not level — they arrive at full value.** `max_level: 1` with `levels: [400, 400]`. And the
fixed value is large: a grade-31 Chaos rune grants **400** `melee_critical_attack` outright, against
**100** for a common attack rune driven all the way to level 20. Roughly 4x, with no duplicate grind.

**The cost is the roll.** Each of the 44 stats sits at about **4.83%**, so landing one specific stat
is roughly a 1-in-21 chance. Chaos runes are powerful and *targeted only in expectation* — advice
should frame them as "reroll until you hit X", never as "slot a Chaos rune for X".

Chaos runes exist at **grade 31 (12) and 41 (12)**, three per weapon/accessory category and one per
armour slot. Since every synergy is grade 41, only the grade-41 Chaos runes can participate in one.

### Where runes go

`equipmentCategory` shows **18 runes each** for `weapon`, `ring`, `necklace`, `bracelet`, `belt`,
`earring`, and only **4 each** for `head`, `chest`, `hands`, `legs`, `feet`, `cloak`. The armor
slots are the 4.0 "Armor Runes" layer, and their shallow pool means armor rune choice is far more
constrained than accessory rune choice — worth saying rather than implying equal freedom.

### Synergies are ordered permutations, and gated at grade 41

**78 synergies, every one of them grade 41.** Six per equipment category, which is exactly `3!` —
so a synergy is an *ordered* arrangement of attack/defense/assist across the three sockets. Order
matters; the same three runes in a different sequence give a different bonus, or none.

```jsonc
{ "id": "rune_synergy_Weapon_41_defense_assist_attack",
  "name": "DEFENSE ASSIST ATTACK",
  "grade": 41,
  "stats": { "int": 4, "debuff_taken_duration_modifier": -350 },
  "combination": ["defense", "assist", "attack"],
  "equipmentCategory": "weapon" }
```

Two consequences: synergy bonuses are unavailable below grade 41, so telling a player with common
runes to "arrange them for the synergy" is wrong; and because `combination` is ordered, a
recommendation must state the **sequence**, not just the set.

Note the data contains an `equipmentCategory: "test"` group of 6 — a questlog artifact, filter it out.

### Levelling economics

Runes level by **consuming duplicates**, one level per duplicate. That makes rune progression a
volume problem rather than a currency problem, and it is why rune headroom stays large on
characters who are otherwise well geared. Unlocking a socket needs a **Rune Hammer**, craftable from
Rune Fragments. *(Duplicate-consumption and Rune Hammer come from community guides, not the API —
confirm before quoting exact fragment counts.)*

## Redfrost and purification — the Nix acquisition loop

Introduced in **4.0.0 "The Frozen Divide: Nix"**. Researched 2026-08-04 from the official update
notes and confirmed against live API data. This is a two-stage acquisition chain and modelling it
as an ordinary drop gets the answer wrong in both directions.

**The loop.** Redfrost items drop **only in Nix**, sealed and unusable. You carry them to
**Shemir's Armillary Sphere** (also translated "Shermir's Celestial Sphere") and **purify** them,
which reveals either real equipment or **Embers of Shemir**, a crafting material. Embers craft Nix
equipment **with certainty** — so the chain has a deterministic floor that a plain drop never has.

### Verified from the API

`database.getItem` on `sealed_staff_b_001` ("Redfrost Staff"), 2026-08-04:

- **`mainCategory/subCategory` is `misc/sealedequip`.** This is why they are absent from
  `getEquipmentItems` — that catalogue is equipment-only. Search `sealedequip` to enumerate them.
- Ids are prefixed **`sealed_`**: `sealed_staff_b_001`, `sealed_orb_b_001`.
- **The purification cost is in the item's own `description` text** — "Flame of Purification Cost:
  250" — as embedded HTML with colour spans, so it needs stripping before display.
- `isExchangeable: false`, `isStorable: false`, `sellPrice: 0`. They cannot be traded or banked,
  which matches the rule that they vanish when you leave Nix.
- **231 NPC sources**, each at `p = 0.00014583` (**0.0146%**).
- **11 resource/object sources**, and these are far better: **Mystery Chest 0.35%**, Rift Gold
  Coffer 0.26%, Rift Jar 0.117%.

**Chests beat mobs by roughly 24x for this item.** That single comparison is exactly the kind of
concrete routing advice the tool exists to give, and it is computable from data we already have.

### Purification costs, from the official notes

Two currencies: Sollant **and** Flame of Purification.

| Category | Grade | Sollant | Flame |
| --- | --- | --- | --- |
| Equipment | Advanced | 25,000 | 250 |
| Equipment | Rare | 75,000 | 750 |
| Equipment | Hero | 202,000 | 2,000 |
| Equipment | Special | 404,000 | 3,000 |
| Rune | Advanced / Rare / Hero / Special | 12,500 / 37,500 / 75,000 / 150,000 | 150 / 300 / 1,000 / 1,500 |
| Misc | Advanced / Rare / Hero | 10,000 / 30,000 / 50,000 | 100 / 250 / 750 |

### The Flame cap is the real constraint, and advice must lead with it

**Flame of Purification is capped at 8,000 per week** from hunting and gathering. Sources: the Nix
Region Quest Log (5,000 per session), Afterimage monsters (2–9 each), field objects (15–200).
Request forms and item conversions are *not* capped.

At 2,000 Flame per Hero-grade purify, **8,000 a week is four Hero purifications** — and that, not
the drop rate, is what actually paces progression here. Any advice about Redfrost that ignores the
weekly Flame budget is planning against a resource the player cannot actually spend.

### Other hard constraints

- **Redfrost Bag is 22 slots** (1 safety + 21) and **cannot be expanded**.
- Items are **lost** on death in Nix, on leaving Nix by any means including teleport, and on
  logging out. Only the single safety slot survives, and only within Nix.
- Purification is slot-by-slot and **interrupts if you take damage**.

### What is NOT verified — do not state these as fact

- **Named versus generic Redfrost items.** A player reports that a *named* variant
  ("Redfrost Helmet of Nine Lives") has a much higher chance of yielding that specific item with a
  lower ember rate, while a generic "Redfrost Helmet" can yield any helmet with a higher ember rate.
  **The official notes do not distinguish the two**, and it has not been confirmed against the API.
  It is plausible and it matters a great deal for expected cost, so it is worth confirming — but
  until then the advice engine must not quote different rates for the two.
- **Ember counts per craft.** The notes say the quantity "varies depending on the type of
  equipment" and never give numbers.
- **Per-item purify yield probabilities.** Not published.

`PurifyChainEstimator` models this chain and takes those unknowns as caller-supplied parameters
precisely so that it cannot silently invent them.

## Buff systems — Amitoi, morphs, food

Stacking these is a major progression axis, and it is largely invisible in gear-focused advice.
Treat it as first-class.

### Amitoi — and where tab 7's numbers come from

Amitoi are collectible companions that follow the player, pick up loot, heal, and flag
Mystic Globe / Portal locations. The progression-relevant part is that **collecting the right
combinations grants permanent stat bonuses** — from raw Amitoi count and from **Pal Synergy**.

There are **38 Pal Synergies**, and their caps line up exactly with tab 7:

| Pal Synergy bonus | Per synergy | Cap |
| --- | --- | --- |
| Sollant Bonus | +0.3% – +1% | **9.1%** |
| EXP Bonus | +0.5% – +1.5% | **13%** |
| Mastery Bonus | +0.3% – +1% | **7%** |
| Item Chance | +0.5% – +1.5% | **8%** |

**This is the missing explanation for the Miscellaneous tab.** The observed character sits at
EXP Bonus **+10% of a possible 13%** and Item Chance **+3% of a possible 8%** — so Amitoi
collection is a concrete, quantifiable gap with a known ceiling, not a vague "collect more pets"
suggestion. Advice can say exactly how much economy throughput is being left on the table.

`Amitoi Healing +637.28%` on tab 1 is a separate Amitoi-driven stat.

### Morphs

Transformations used for travel — **Glide, Aquatic and Dash** for air, water and land. They
**level up**, granting additional traits and boosts, so they are a progression track rather than
pure convenience. Lower priority than gear or Amitoi, but not zero.

### Food and cooking — the stacking rule that matters

Four categories:

- **Attack** — PvP hit, all hit, heal damage boost, bonus damage, skill damage boost
- **Defense** — all endurance, all evasion, heavy attack
- **Utility** — max health, health recovery, health regen, mana regen
- **Miscellaneous** — currency generation, fishing mastery, EXP bonus

**The rule: two combat buffs can be stacked, but Attack and Defense cannot be stacked together.**
Valid pairs are Attack + Utility, or Defense + Utility. An advice engine that says "eat your
attack food and your defense food before the boss" is recommending something the game will not
let the player do, and that single error would undermine trust in everything else it says.

**Food quality matters** — better ingredients raise the chance of higher-quality results, which
give longer durations and larger stat values. So the cooking input is itself an optimisation
target, not just the output.

> **Currency warning.** The food-stacking specifics above come from guides predating the 4.0.0
> item revamp. The Amitoi Pal Synergy caps corroborate against live tab 7 values and are
> trustworthy; **the Attack/Defense stacking restriction has not been verified against 4.5.0**
> and should be confirmed in-game before the advice engine relies on it. Flagged rather than
> quietly assumed.

## Currencies — left to right along the top bar

Authoritative; supplied by a player of the account, then cross-checked. **Only two of these
are relevant to gear progression.** The assistant must never recommend spending the others on
upgrades, and must never treat them as fungible with Sollant.

| # | Currency | What it is | Use for progression? |
| --- | --- | --- | --- |
| 1 | **Lucent** | The auction-house trading currency. **Two sources**: bought with real money in the cash shop, *or* **earned by selling sealed loot to other players**. | **Depends on how it was obtained** — see the correction below. Earned Lucent is a legitimate progression currency; purchased Lucent is a purchase. |
| 2 | **Sollant** | The game's gold. Earned in play. Crafting, upgrades, merchants, most sinks. | **Yes — the primary progression currency.** |
| 3 | **Contract Coin** | Earned from contracts. Spent at the Contract Merchant. | **Yes** — merchant-gated, so advice must name the merchant. |
| 4 | **Guild Coin** | Earned through guild activity. Spent at the Guild Merchant. | **Yes** — guild merchant only. |
| 5 | **Restoration Coin** | Restores a downed character on death. | **No.** Never a progression input. Do not suggest saving or spending it for gear. |
| 6 | **Ornate Coin** | Compensation grants from the developers for downtime, plus exploration rewards. Buys cosmetics, and some boosts (e.g. runes) in the cash shop. | **Partially** — the rune boosts are the only progression-relevant sink. Cosmetics are not progression. |
| 7 | **Loyalty Points** | Accrued by spending real money: **1,000 points per $99.99 spent.** Buys rare and best-in-slot **cosmetics**. | **No.** Cosmetic only. Never present as a gear path. |
| 8 | **Character Boost Ticket** | **$49.99 each**, purchasable repeatedly. Boosts a new character to level 55. | **No.** A real-money alt-levelling item, not a progression currency. |

Three rules that fall out of this table and must be enforced in the system prompt:

- **Never recommend a real-money purchase as a progression step.** Loyalty Points and Boost Tickets
  are dollars, and *buying* Lucent is dollars. If a path genuinely requires them, say the dollar cost
  plainly and present it as optional.
- **Restoration Coin is not a resource to optimise.** It has exactly one use and it isn't gear.
- **Loyalty Points and most Ornate Coin spending buy cosmetics.** Cosmetics are not progress.

### Correction: Lucent is earnable, and treating it as pure real money gives bad advice

An earlier version of this table called Lucent "real money — never recommend spending it as if it
were earned". **That is too strong and it suppresses correct advice.** Lucent is the auction-house
currency, and a player can earn it by **selling sealed loot to other players**, with no money spent.
Guides are explicit that you can fund your purchases "without consuming any real money".

So the rule is about **provenance, not the currency**:

- **Lucent the player earned by selling** is an ordinary progression currency. Recommend spending it
  freely, and recommend *earning* it — selling sealed drops is a legitimate, often optimal play.
- **Lucent bought from the cash shop** is a purchase. Price it in dollars, mark it optional.
- Loadstar **cannot tell the two apart from a screenshot** — the currency bar shows one number. So
  never assert which kind a player holds. Say "if you earned this, here is the buy; if you would have
  to purchase Lucent, here is what that costs in dollars" and let them decide.

This matters most for Potential abilities: sealed items keep their Potentials through a trade, so the
auction house is the deterministic route to a specific roll. Ruling Lucent out entirely would have
hidden the only non-random path to it.

### Sealing — the mechanism behind the whole Lucent economy

Loot is not tradable by default. **Sealing** makes it so, and 4.0.0 replaced the old 'Rubbing' bag
option with 'Seal'.

- Sealing consumes a **Seal Key** plus **Sollant**, and the Sollant cost **varies by item**.
- **Breaking a seal is a one-way door**: once broken the item can never be traded again. So every
  drop is a fork — use it, or sell it — and the choice is irreversible. Advice should name that
  explicitly before recommending either side.
- **Potentials survive sealing**, which is what gives sealed items their premium.

Auction house constraints that bound any "sell it" recommendation:

- Unlocks at **character level 40**.
- **Tax is 20% base plus the server's Castle Tax Rate**, deducted from proceeds — so quote net, not
  list price.
- **Maximum 30 active listings.**
- Listing costs **Sollant** up front, so selling has a floor cost even if nothing sells.
- **Gear that has been upgraded, or obtained in Co-Op Dungeons and Guild Raids, cannot be sold.**
  That last exclusion is significant: much of a geared player's best loot is *ineligible*, so
  "sell your old gear" is often wrong for exactly the items they most want to offload.

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

## Item level 51 / 61 / 71 — when T4 gear actually beats the free T3

**The single most common way to give a player a worse item.** Reported by a player of the game
2026-08-04, and partially confirmed against the catalogue.

New and returning players are given **fully-traited T3 gear for free**. T4 gear does not
automatically beat it — it only overtakes at **item level 51, 61 and 71**. Below those points a
shiny T4 drop can be a sideways move or an outright downgrade, because the free T3 already has its
traits unlocked and levelled while the T4 piece arrives bare (see the 4.0.0 rule: gear now drops
with **no** traits).

Confirmed against `getEquipmentItems` by taking the **median primary stat per slot across every
item in each tier band** (T3 tables run 21–50, T4 tables run 51–80). As a percentage of a maxed
level-50 T3 piece:

| Slot group | T4 @51 | T4 @61 | T4 @71 | T4 @80 |
| --- | --- | --- | --- | --- |
| Accessories (ring, necklace, earring, belt, bracelet) | 86% | **98%** | **109%** | 120% |
| chest / head / legs / feet / hands | 89–90% | **102–103%** | **115–116%** | 126–128% |

So **51–60 is worse, 61 is parity, and 71+ is genuinely better** — the player report is confirmed by
the catalogue. Two exceptions: `cloak` is already at parity at 51 and scales fastest (145% at 80),
while `gauntlet` is the worst offender at only 63% of T3 at level 51, not reaching parity until ~71.

**Most weapons do not scale with item level at all.** Bow, crossbow, dagger, staff, spear, sword2h,
orb and wand are flat across the whole 51–80 range; only `sword` and `gauntlet` scale. Never tell a
player their bow got stronger because its item level went up.

The full per-slot table ships to the advice model as
`src/Loadstar.Games.ThroneAndLiberty/Knowledge/03-gear-tier-crossover.md`.

Consequences for advice:

- **Never recommend a T4 piece below level 51 over an established T3 piece.** It is not an upgrade.
- Between 51 and 71, the comparison depends on how well-traited the T3 piece is. Ask, or say the
  comparison depends on it, rather than assuming the higher tier wins.
- This compounds with Succession: a T4 piece is worth taking as **level fuel** long before it is
  worth wearing, which is a different recommendation with a different action attached.
- Re-check these numbers after any patch. They are balance figures and will move.

## The trait economy — two scarce currencies at once

Verified from `database.getItem` on 2026-08-04, plus player-reported figures marked as such. This is
the real bottleneck on a geared character, and it is routinely under-priced in advice.

**Sollant, from `itemResonanceCost`:**

| Action | Sollant | Materials |
| --- | ---: | --- |
| **Open** one Trait Resonance slot | **1,500,000** | 3 × Trait Resonance Stone |
| **Change** one slot | **1,500,000** | 3 × Trait Resonance Stone |

Four slots per item, so **6,000,000 Sollant to open all four on one piece** — and correcting a
mis-pick costs the same as making it. A resonance mistake is a 1.5-million-Sollant mistake.

**Unlockstones (player-reported):** **25 Heroic Unlockstones** per heroic trait, **100 regular
Unlockstones** per regular trait. Primarily **weekly-capped**, so it paces progress rather than
pricing it — the same shape as the Flame of Purification cap. Heroic traits are *selected* on
unlock, so the pick commits the moment the stones are spent.

**Three sources, every one weekly-bounded:**

1. The weekly allotment.
2. **Lithographs** — `database.getLitograph?input={"id":"60"}` (note the spelling, one `h`). Verified:
   "Garments of Quiet Wisdom" consumes **3 chest pieces** and outputs **6 × Trait Unlockstone
   Fragment + 2 × Trait Resonance Stone Piece**; fragments craft into a Trait Unlockstone with
   Precious Magic Powder. **Surplus gear is therefore not vendor trash** — it is usually the
   cheapest unlockstone route a player has, and it costs them nothing they were using.
3. **Fortitude Coins** from daily dungeon chests, craftable into Unlockstones — but runs are gated
   by **Dimensional Contract Token I and II**, finite per week. A **limited-time field-boss PvP
   event** adds a temporary extra source; worth prioritising while it runs, never worth planning
   around long-term.

Consequences: price every trait recommendation in **both** currencies; never recommend stones on a
piece about to be replaced; check for surplus gear before recommending any grind; and because every
route is capped, **the order of spending matters more than the total**.

## Gear sets — threshold bonuses by piece count, and builds run several at once

Verified 2026-08-04 against `characterBuilder.getEquipmentItemSets` (~149 KB, **78 sets**) and
cross-checked on a live build. Sets work like artifact sets: equipping N pieces of the same set
unlocks a bonus at each threshold. **Most questlog builds are built around one**, so the advice
engine should always have a set in view.

```jsonc
{ "id": "set_aa_fabric_001", "name": "Mother Nature Set", "grade": 41,
  "itemSetMadeOfItems": [ { "id": "head_fabric_aa_t1_nomal_001", "sub_category": "head", … } ],
  "itemSetBonus": [
    { "set_count": 2, "bonus_stat": [], "bonus_passive": [{ "text": "Weaken Duration +7.5" }] },
    { "set_count": 4, "bonus_stat": [{ "type": "attack_range_modifier", "value": 1000 }],
      "bonus_passive": null } ] }
```

Two properties of that shape drive how it must be used:

- **`bonus_stat` is machine-readable** — `type` is a stat id and `value` is summable, so it feeds a
  derived stat total directly. **`bonus_passive` is text only**; quote it, never parse a number out
  of it.
- **Item → set exists only in reverse**, through `itemSetMadeOfItems`. Build the inverse index once.

Observed on `GoldenConquestAndWriter` (T4 Seeker Magic HAE/END) — a single build runs **three sets
simultaneously, at different completion levels**:

| Set | Pieces | Bonus state |
| --- | --- | --- |
| Greedseeker (artifacts) | 6 | all active — 2pc `all_critical_defense 1000`, 4pc `all_state_tolerance 1200`, 6pc `skill_power_resistance 1250` |
| Frigid Melody | 4 | 2pc + 4pc active, both passive text |
| Prayer of Salvation | 1 | **2pc is one piece away** — Max Health +2200, plus a damage-reduction proc |

That last row is the pattern worth chasing. A set sitting at 1/4 whose 2-piece bonus is a single
item away is a cheap, concrete gain that is **completely invisible from item level alone** — and per
the least-resistance rule at the top of this file, that beats recommending the full four-piece.

Note the artifact set appears in the same structure as armour sets, confirming the six artifact
slots documented above are set-driven too.

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

## The game install and its files — a checked dead end

Searched 2026-08-04 for boss schedules, localisation strings or item data. **There is nothing usable,
and the app must never read these files anyway** — docs/anti-cheat-posture.md forbids "reading or
modifying game files, configs, or packets" as client tampering, and **EasyAntiCheat ships in the
install directory**. This note exists so the search is not repeated.

**Install directory** (`…\steamapps\common\Throne and Liberty`, ~84 GB): a standard Unreal layout of
`EasyAntiCheat`, `Engine` and `TL`. Everything gameplay-related is inside **IoStore containers** —
24 `.ucas` (57 GB), 42 `.pak` (13 GB), 33 `.utoc` — plus 2,084 `.bk2` Bink videos. There are **no
loose data files, no localisation folder and no logs**. The only config is a **162-byte**
`UserGame.ini` containing movie-player settings.

Reading any of it would mean unpacking IoStore archives, i.e. datamining, which needs the AES key and
external tooling. Not worth pursuing: the same knowledge is obtainable from the in-game UI captures
this project already does, and it could never be shipped as a runtime feature.

**User data** (`%LOCALAPPDATA%\TL\Saved\`) — closer to useful, still not useful:

| File | Size | What it actually is |
| --- | --- | --- |
| `Config\EventBoard.ini` | 1.4 MB | The **cash-shop bulletin board cache** — battle passes, bundles, Character Boost. Promo HTTP payloads, not schedules. |
| `Logs\TL-backup-*.log` | up to 2 MB | **Pure engine diagnostics** — LogStreaming, LogAnimation, LogPhysics, LogD3D12RHI. Zero hits for Boss, Spawn, Schedule, Siege, Timer or Calendar. No gameplay data at all. |
| `Config\NCStorageLocalData.ini` | 727 KB | Per-character **UI state**, keyed by account and character GUIDs — chat tabs, alarms, HUD layout. Includes `KCONTENTSALARM`, likely the Content Settings toggles. Contains personal identifiers; do not read or log it. |
| `Saved\Crashes\` | — | UE minidumps. |

**Conclusion: the client's own UI remains the best source**, exactly as the boss-timer work found.
The map's schedule panel and Content Settings window give times and an icon index through ordinary
screen capture — no file access, no datamining, and inside the anti-cheat contract.

## Boss timers — what the in-game map actually shows

**Captured from a live client 2026-08-04** (process `TL`, version 1.443.22.7936), which settles
several open questions and contradicts one assumption in docs/boss-timer.md.

The map's schedule panel has **Hourly** and **Daily** tabs. **Both are live at once and they carry
different things** — established 2026-08-06, and it is the single most important fact about the
schedule:

| Tab | Carries | Recurrence |
| --- | --- | --- |
| **Daily** | Siege and **archbosses** only | per weekday, and some weekdays are empty |
| **Hourly** | The **regular** field bosses (Talus, Excavator-9, the rest) | **every day**, 7 slots, composition rotates |

So a day with archbosses has **both** streams running, and the Daily tab alone is not the schedule.
Modelling only Daily meant that on a weekday it leaves empty — Thursday and Monday — the countdown
reached forward two days for the next archboss while seven field bosses were spawning that same
evening. That was reported from the field as "a third field boss that says it is in 48h 27m".

`docs/boss-schedule.json` now expresses three streams: `weeklySlots` (Daily tab), `hourlySlots`
(Hourly tab, additive across every day) and `datedSlots` (explicit dates, for monthly events like the
Vienta tax delivery). Slot times are **UTC** and weekday keys are UTC weekdays, so evening Pacific
slots roll to the next UTC day.

The rest of this section is the original Daily-tab capture. On Daily it lists one row per date with
event icons under a time:

| Date | Slots |
| --- | --- |
| 04/08 Tue | 17:00, 20:00 |
| 05/08 Wed | 17:00, 20:00 |
| 06/08 Thu | **none** |
| 07/08 Fri | 17:00, 20:00 |
| 08/08 Sat | 17:00, 20:00 |
| 09/08 Sun | **18:00 only**, a single event |
| 10/08 Mon | **none** |
| 11/08 Tue | 17:00, 20:00 |
| 12/08 Wed | 17:00, 20:00 |
| 13/08 Thu | **none** |
| 14/08 Fri | 17:00, 20:00 |

**The weekly shape is now clear, and it is not "the same slots every day":**
**Thursday and Monday are empty**, **Sunday at 18:00 is SIEGE** — confirmed by a player of the game,
which is why it sits at a time no other day uses and shows a single orange icon rather than the
usual cluster — and the remaining days run **17:00 and 20:00**. Boss *count* also varies per slot:
some show one icon, others four or five.

Siege being its own thing matters for advice as well as for the timer. It is scheduled guild PvP,
not a boss to travel to, so a countdown that labels it "Field Bosses" is wrong, and the PvP/PvE axis
rules apply to how the player should be geared for it.

**The events are icons with no text** — the same identification problem as the currency bar and
inventory. But the game solves it for us, and this is the important discovery:

### Content Settings IS the icon index — capture it once

The map's **Content Settings** window lists **every boss beside its own icon, with its name in
text**. That is precisely the named-currency-reference pattern, already provided by the game: one
user-initiated capture builds a complete icon → name lookup, after which the schedule's icons are
resolvable deterministically and offline.

**Prompt for this capture on first run**, exactly like the currency reference. It is the difference
between "Field Bosses at 20:00" and "Ascended Morokai at 20:00".

The window has three sections, each independently toggleable — and the toggles matter, because a
boss the player has unticked will not appear on their schedule at all:

**Boss (~38 entries).** Most exist in both normal and *Ascended* form:
Manticus · Leviathan · Pakilo Naru · Daigon · Kowazan · Ahzreil · Junobote · Grand Aelon · Nirma ·
Aridus · Malakar · Adentus · Minezerok · Talus · Cornelius · Chernobog · Excavator-9 · Morokai ·
Porfos · Thuban, plus **Ascended** variants of Kowazan, Ahzreil, Junobote, Grand Aelon, Nirma,
Aridus, Malakar, Adentus, Minezerok, Talus, Cornelius, Chernobog, Excavator-9, Morokai, Manticus,
Leviathan, Pakilo Naru and Daigon.

**Archboss (9).** Fell Tree · Ascended Giant Cordy · Ice-Cold Heart · Ascended Deluzhnoa ·
Desert Overlord · Ascended Queen Bellandir · Courte's Wraith · Ascended Tevent · **Ramux**
(the 4.5.0 addition).

**Boonstone (6).** Bercant Manor · Black Anvil Forge · Swamp of Silence · …of the Great Tree ·
Quietis's Demesne · Grayclaw Forest. **Unticked by default** on this character.

> **`boss-schedule.json`'s roster of seven is badly incomplete.** It lists Adentus, Talus, Grand
> Aelon, Chernobog, Cornelius, Junobote and Daigon — against ~38 bosses and 9 archbosses actually in
> the game. It came from `getFieldBossEntries`, so that endpoint is not the roster to build on.
> The client is.

**The good news, and it is substantial:**

1. **A day can be empty.** Thursday has no slots at all, so a schedule model that assumes every day
   is identical is wrong. The bundled JSON's flat `dailySlots` cannot express this and needs a
   per-weekday shape.
2. **Times are unambiguous and server-local**, straight from the player's own client — which is the
   authority for their server, unlike anything external.
3. **The observed times do not match questlog.** The client shows **17:00 / 20:00**; questlog's
   Americas grid shows **19:00 / 22:00 Field Bosses**. A two-hour gap. Until that is explained,
   **trust the client and treat the bundled Americas table as unverified** — this is exactly the
   viewer-timezone hazard flagged in `boss-schedule.json`'s `$timezoneWarning`.

**The currency bar was fully expanded and readable in the same capture** — nine values including
Sollant at 169,661,552 — confirming the expanded bar is genuinely machine-readable, which the whole
budgeting side of the product depends on.

> **The privacy mask that blacked out the bottom-left corner is gone, removed 2026-08-10.** It was
> doing real damage: on a character-sheet capture it covered the stat column, which is the single
> highest-value region in the game. It could not simply be moved, because the chat window it targeted
> is draggable and resizable — and detection was measured and rejected. A full-resolution capture
> shows chat is drawn as **plain text straight onto the game world**: no border, no background fill,
> no anchor, so there is no shape to find. The only distinctive signal is the vivid green of player
> names, which would cover names but not the white message text, and green is also uncommon-item
> colour. Any *fixed* rectangle also fails on the character sheet regardless of who places it, since
> chat is hidden behind that panel while the mask is not. The protection that replaces it is the ask
> window: every queued image shown full size with a delete button, which the player can verify.

**The Hourly tab was examined 2026-08-06** and turned out to be a second concurrent stream rather
than another view of the same one — see the top of this section. Seven slots a day in Pacific time:
11:00, 14:00, 18:00, **18:30**, 21:00, **21:30**, 23:00, with nothing before 11:00 or after 23:00.

Two of those matter beyond the times. **18:30 and 21:30 are guild PvP every day** and are the only
single-event slots; the other five are peace and rotate randomly through the regular roster. That
makes contest mode derivable **from the slot time** for this stream, with no hovering and no per-boss
data — which is worth knowing because the mode badge drawn on each icon does *not* survive screen
capture (a ~20px icon downsamples to ~11px and its corner badge to three or four pixels).

Rotating slots stay labelled generically. Naming one would be wrong most days, and wrong
confidently.

## Boss timers — how the feature is built

Not a live feed. There is no spawn API to poll, so the timer computes everything locally from a
captured table: **three concurrent slot streams** per region (Americas / Europe / Asia), plus the
03:00 reset. Times are stored in **UTC**, so one instant resolves identically for every player.

**There is no server-timezone setting.** It was removed — nobody except the game knows what zone a
server runs in, so asking the player produced a confidently wrong countdown whenever they guessed. A
`defaultTimeZone` per region survives in the file as a display default, not a fact about any server.

**The schedule is published, with a bundled fallback.** `docs/boss-schedule.json` is served by GitHub
Pages and fetched on startup, so a rotation change is one commit and no release. The same file is
embedded in the assembly as the offline copy, and a download is validated before it is adopted —
being offline is the normal state for a desktop app, and the bundled table is stale rather than
wrong. That is the one place Loadstar depends on something staying up, and the fallback is what makes
it acceptable.

Refreshing it is a skill: **`/capture-boss-schedule`**, which reads the player's own client, converts
to UTC, validates the conversion arithmetic against a retained `localPst` on every slot, checks boss
names against a closed vocabulary in `icon-legend.json`, and publishes.

## questlog.gg API

Public tRPC, undocumented, unversioned. Base
`https://questlog.gg/throne-and-liberty/api/trpc/`.

`characterBuilder.getCharacter?input={"slug":"<build-slug>"}` — **`slug` is the only parameter**,
and it is the *last* path segment of a build URL. An earlier note here claimed `url` was also
required; that was wrong — `url` is not a parameter, and passing it is simply ignored.

**The response shape is the part that bites.** `builds` is a **sibling of `character`**, not a
field inside it:

```jsonc
{ "character": { ...metadata... },   // name, level, tags, desc, folders — NO equipment
  "builds":    [ { ... } ],          // the actual loadouts
  "folders":   [ { ... } ],          // organisational only: id, name, note, color, order
  "status":    "..." }
```

Reading `character` and expecting gear there returns nothing, silently. Equipment lives in
`builds[]`.

**A character holds multiple builds.** The reference build below has **six**. Each entry carries:

```
{ id, name, userId, note, characterId, attributes, equipment, order, weaponTypes, folderId, tags }
```

So an importer must let the user **choose which loadout** rather than assuming one — and
`attributes` on each build is the **target stat spread**, which is exactly what pairs with the
breakpoint and cost-escalation work above.

Per-slot equipment shape (real sample):

```jsonc
"belt": {
  "id": "belt_aa_S1_003", "perk": null,
  "runes": { "0": { "lvl": 120, "runeId": "Belt_Ast_Rune_Usable_kAA2_001", "statId": "skill_power_resistance" }, … },
  "heroic": { "1": "hp_max", "2": "cost_max", "3": "all_armor" },
  "traits": { "hp_max": 600, "skill_power_resistance": 800, "debuff_taken_duration_modifier": -600 },
  "potential": "range_armor", "resonance": "hp_max"
}
```

Note traits can be **negative** (`debuff_taken_duration_modifier: -600`).

### The equipment catalogue — what it actually contains

Fetched and inspected 2026-08-04:
`characterBuilder.getEquipmentItems?input={"language":"en"}` → **10.4 MB, 1,773 items**, returned
as a JSON **object keyed by item id** (not an array). Per item:

| Field | Notes |
| --- | --- |
| `name` | **Display name.** This is what makes "never invent an item name" enforceable rather than aspirational — identification becomes a local lookup. |
| `id` | Encodes weapon/slot, rarity, tier and source: `bow_aa_t5_boss_001`. |
| `icon` | Asset path, for the local perceptual-hash icon index. |
| `grade` | Rarity ladder, five values: **11 (180 items), 21 (208), 31 (420), 41 (894), 51 (21)**. 50 items carry no grade. |
| `equipmentType` | 40 values — `chest`, `head`, `ring`, `bow`, `belt`, `riftstone`… The clean filter for "alternatives for this slot". |
| `requiredLevel` | Character level gate. |
| `setId` | Set membership, so the `(4/5)` completion cliff is computable. |
| `itemStats.main` / `.extra` | **Keyed by item level, 0–85.** So the stat value of an item at any level is a lookup. |
| `itemStats.traits` | Trait progression as space-separated pips, e.g. `"200 400 600 800"`. |
| `itemStats.resonance` | Per-stat roll probabilities. |

**Item level 85 exists for exactly 20 items**, which matches the 4.5.0 note that Archboss weapons
sit at a fixed item level of 85. Good cross-check that this data is current.

#### The bulk catalogue is the wrong endpoint for costs and drops — use `database.getItem`

`getEquipmentItems` carries no acquisition data and no upgrade costs, and it is easy to conclude
from that alone that the API does not have them. **It does.** They live on a different procedure,
found by watching what questlog's own item page requests (see below). Reach for `database.getItem`
before telling anyone a cost has to be captured from the screen.

What `getEquipmentItems` is still the right tool for: bulk work. It is the one call that gives all
1,773 items at once, which is what the icon index and any "find alternatives for this slot" scan
need. Per-item detail is a second call.

**"Easy to acquire" from the bulk catalogue alone is an inference**, since it has no source column —
the id carries a token (`nomal`/`normal` 416, `boss` 80, `Arch` 30, `upgrade` 37) and `grade`
correlates with rarity. Use that only to shortlist. Once a candidate matters, `database.getItem`
gives the real answer, including drop rates.

**Watermark interaction.** "Upgrade to the next bracket" is not one question. Raising raw power and
raising the Equipment watermark are different goals, and the watermark is an average of three
category maxima — so an upgrade in an already-leading category adds power and moves the watermark
by nothing. Any upgrade planner must say which of the two it is optimising.

### tldb.info — do not use it, on two independent grounds

Investigated 2026-08-04 as a source for drop locations, which questlog does not carry. **It is not
usable**, and both reasons are disqualifying on their own.

**1. It is two major versions stale.** The site states its own version in two places — a banner
reading "TLDB has been updated for Patch **3.18.0**" and a footer reading "Version: 3.18.0". The
live game is **4.5.0**. That gap spans **4.0.0**, the update that rewrote item progression, so its
data predates the single change this document warns most loudly about.

Verified at the data level rather than taking the version string's word for it: all four item-level
**85 Archboss weapons** that questlog returns by name — `sword2h_aa_S1_arch_001`
("Last Dragon's Thunder Greatsword"), `sword_aa_S1_arch_001`, `dagger_aa_S1_arch_001`,
`crossbow_aa_S1_arch_001` — return **HTTP 404** on TLDB. Corroborating detail: TLDB item pages still
render `enchantable` and `enchant_transferable` capability icons, and Enhancement and Transfer were
*removed* in 4.0.0. Its data model is the pre-rewrite one.

**2. The licence does not cover this use.** TLDB offers tooltip syndication — an `embed.js` script
that decorates links on a **website**. Their terms say the content is licensed "solely through the
original tooltip syndication script/instructions" and explicitly "does not grant you a license to
do whatever you want". Loadstar is a desktop overlay, not a website, so the sanctioned mechanism
does not apply to it, and reusing the underlying data outside that script is not granted. There is
also **no JSON API**: the site is SvelteKit, server-rendered, and an item page issues no data
request at all — so any programmatic use would be scraping.

**The one genuinely useful thing learned from TLDB: it shares questlog's item ids.**
`sword2h_aa_t2_raid_001` resolves on both. So if a drop-source dataset ever appears — theirs once
current, or another — it joins to the questlog catalogue on `id` with no fuzzy matching.

**Where drop locations should come from instead.** The game client knows, and the player can show
it: item and codex screens name sources. That makes it another **user-initiated capture**, the same
pattern already used for the Combat Power tooltip and the named-currency reference. Until that
exists, advice may name *what* to upgrade and *what it gains*, and must not invent *where it drops*.

### `database.getItem` — drops, upgrade costs and prices, and it answers the headline feature

Found 2026-08-04 by loading questlog's own item page and watching its network calls. This is the
endpoint the product's core request depends on:

```
GET database.getItem?input={"id":"bracelet_aa_t3_normal_001","language":"en"}
```

~23 KB per item. Alongside the catalogue fields it carries four things nothing else does:

**`itemDroppedFromNpcs`** — "kill xxx". Per entry: NPC `name` and `id`, NPC `level`, `quantity`,
`dropType`, `dropCondition` (e.g. `dungeonPointDrop`), `mainCategory` (e.g. `party`), and
**`probability`** as a real drop rate — `0.00751705` on the sample, i.e. about 0.75%. So advice can
name the mob *and* be honest about the grind, which matters: "kill X" reads very differently at 25%
than at 0.75%.

**`itemEnchant`** — **the upgrade cost table, per level.** Twelve entries on the sample, each with
`requiredGold` (11,000 at level 1, 14,600 at level 2 — escalating), `requiredItems` with named
materials and **quantities** ("Noble Accessory Growthstone" ×1, then ×2), plus `resultProbabilities`
giving the C/B/A/S outcome distribution and `enchantPoint` per result, and an `overflow` value.

This makes **"how much to reach the next bracket" fully computable**: sum `requiredGold` and
`requiredItems` across the level span, and use `resultProbabilities` for expected attempts rather
than quoting a best case. Quote the expected cost, not the floor.

**`itemIsContainedInItems`** (15 on the sample) and **`itemIsOutputOfRecipes`** — the non-drop
routes: which chests and pouches can yield it, and whether it is craftable. Often the *easy* answer
the player actually wants.

**`auctionHouseId`**, `isExchangeable`, `sellPrice` — the trade route. `sellPrice` 109,090 on the
sample matches the Sale Price seen on a live tooltip, which is a good sign this data is current.

Two companion procedures the same page calls:

| Procedure | Gives |
| --- | --- |
| `auctionHouse.getAuctionItem?input={"language":"en","regionId":"eu-f","itemId":"...","timespan":360}` | Price history for one item, per region |
| `auctionHouse.getAuctionHouse?input={"language":"en","regionId":"eu-f"}` | The whole auction house for a region |
| `map.getMarkers?input={"language":"en"}` | Map markers — the "in yyy" half of "kill xxx in yyy" |
| `statFormat.getStatFormat?input={"language":"en"}` | How to render stat ids as display text |

**Auction prices are in Lucent, and Lucent is real money.** Any advice that prices a route through
the auction house must say so in dollars terms and present it as optional, per the currency rules
above. "Just buy it" is not a progression step, it is a purchase.

`regionId` is a parameter (`eu-f` observed) and must follow the player's server, not be hardcoded.

### Heroic trait values — hidden in `random_stat_group_N`, found by reading questlog's own JS

**The hardest piece of the target-derivation puzzle, and it is not where you would look.** A build's
equipment carries `"heroic": {"1": "hp_max", "2": "cost_max", "3": "all_armor"}` — slot index to
stat id, with **no value**. There is no heroic endpoint: `getHeroicTraits`, `getHeroicStats`,
`getTraits` and six other plausible names all return tRPC 404, so the enumeration is conclusive.

The resolution is in the item itself, under a field easy to miss because most items do not have it.
From questlog's client bundle:

```js
if (n.heroic) for (let [r, i] of Object.entries(n.heroic)) {
  let n = e.itemStats?.[`random_stat_group_${r}`]?.find(e => e.stat_id === i);
  if (n) { let a = n.base_value; /* … `Heroic Effect ${r}` … */ }
}
```

So: **`itemStats.random_stat_group_{slotIndex}` → find the entry whose `stat_id` matches the build's
pick → take `base_value`.** Same structure runes use, which is why it is invisible if you only
enumerate `main`/`extra`/`traits`/`resonance` on an item that happens to have no heroic groups.

Verified on `GoldenConquestAndWriter` — all 8 picks resolve:

| Slot | Item | Pick | `base_value` |
| --- | --- | --- | --- |
| bracelet | Shackled Twilight | `con` / `per` | **8** each |
| off_hand | Calanthia's Doctrine of Chaos | `con` | **14** |
| cloak | Calanthia's Confession | `all_critical_defense` | 1600 |

**Two other constants recovered from the same bundle**, both worth having:

- **The grade ladder decodes**: `MISC:0, COMMON:11, UNCOMMON:21, RARE:31, RARE_T2:32, EPIC:41,
  EPIC_T2:42, EPIC_T3:43, HEROIC:51, ARTIFACT:61, ANCIENT:71`. So the catalogue's `grade: 41` seen
  everywhere is **Epic**, and grade 51 is Heroic — which is the rarity gate on the whole trait
  system.
- The attribute breakpoint ladder is hardcoded there too (`str: {30:{hp_max:750}, 40:{damage_reduction:30}, …}`),
  matching the tables recorded above from tooltips.

**Where the reconciliation stands.** Summing base + item `extra` + heroic + traits + uniqueTraits +
resonance + potential + rune synergies against questlog's own displayed totals:

| | Str | Dex | Wis | Per | For |
| --- | --- | --- | --- | --- | --- |
| computed | 31 | 33 | 94 | 61 | 116 |
| questlog | 42 | 36 | 100 | 67 | 120 |
| gap | −11 | −3 | −6 | **−6** | **−4** |

Heroic alone took Perception from −25 to −6 and Fortitude from −26 to −4. The **residual gap is
almost certainly weapon specialization** — the same bundle references `weaponSpecializationBuildId`
right after the heroic block, and the builder page loads `weaponSpecialization.getWeaponSpecializationBySlug`.
That is the next thing to model; it is a separate build object, not part of `equipment`.

### Reference catalogues — the icon-identification index

The builder page also loads language-parameterised catalogues, which are the local index that
solves the icon-naming problem:

`getEquipmentItems?input={"language":"en"}` · `getEquipmentItemSets` · `getEquipmentRunes` ·
`getRuneSynergies` · `getAttributeStats` · `getPreviewEquipmentItems` ·
`getTraitRecommendations?input={"mainHandType":"wand","offHandType":"bow"}`

Cache these once; they are static per patch.

### The user's target build

**"Seeker PVE Healer"** — `InfernalRavenousUnderTheSalvation`, level 60, tags `pve` + `healer`,
**Wand + Bow**, six loadouts, last updated 2026-08-02.

A healer build reframes the stat work above: Wisdom (Max Mana, Mana Regen, Cooldown Speed) and
Perception (Buff Duration, CC) matter more than raw damage, and the Max Damage families are
largely beside the point. Do not apply DPS reasoning to it.

Cache hard, request rarely, always keep the manual-JSON-paste fallback working.
