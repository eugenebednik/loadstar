# Throne and Liberty — progression systems

Everything here is verified against a live 4.5.0 client. Where something is unconfirmed it says so,
and you must hedge in the same place rather than inventing confidence the data does not have.

## Rank advice by Combat Power headroom, not by intuition

Hovering the Gear Score opens a tooltip breaking Combat Power into current/max per category. **That
is the game telling you where its own headroom is** — prefer it over any reasoning of your own about
which system to push. A reference character showed:

| Category | Current / Max | Headroom |
| --- | --- | --- |
| Runes | 988 / 1530 | **542** |
| Accessories | 1850 / 2357 | 507 |
| Armor | 1650 / 2089 | 439 |
| Weapon Masteries | 1047 / 1400 | 353 |
| Weapons | 1248 / 1464 | 216 |
| Skills | 728 / 840 | 112 |
| Stellar Journey | 462 / 524 | 62 |
| Levels | 350 / 350 | 0 — finished |
| Artifacts | 380 / 380 | 0 — finished |

A category at max is finished; advice to push it is wasted effort. If the tooltip is not in the
capture, ask for it — it converts "where does my next hour go" from inference into a lookup.

## Equipment watermark — an average, so the laggard decides

The watermark is **the average of the highest item level ever obtained in each of three categories:
weapons, armour, accessories**. It sets the floor for everything you are given later.

Consequences that invert the obvious:

- Upgrading your **strongest** category moves the watermark by **nothing**. Only the lagging ones do.
- Only the single highest item **ever DROPPED** per category counts. Equipping, selling, banking or
  destroying it changes nothing — a level 76 armour piece found months ago and thrown away is still
  your armour maximum, permanently.
- Because it floors future drops, raising it can beat a larger one-off upgrade elsewhere.

**You therefore cannot read the watermark off the equipment slots, and must not try.** Per-slot levels
are what is WORN; the watermark is the best ever received, and the two can be far apart. Saying "your
watermark is held back by your weakest slots" is wrong twice over: only the maximum in each category
counts, so the weakest slot is irrelevant, and worn gear is the wrong set of items entirely. Read the
watermark number itself — hovering it gives the three category maxima.

### Price it before recommending it, because the curve collapses at the top

Drops land between **3 below and 1 above** the current watermark, and above 51 never more than +1. So
it climbs one point at a time and the odds of any given drop being that +1 fall away:

| Step | Chance of +1 | Drops needed |
| --- | --- | --- |
| 51 → 52 | 66.7% | ~1.5 |
| 60 → 61 | 64.6% | ~1.5 |
| 69 → 70 | 50.3% | ~2 |
| 74 → 75 | 32.5% | ~3 |
| 79 → 80 | **5%** | **~20** |

All three categories to 80 is roughly 257 drops at best, over 300 realistically.

**This flips the advice depending on where the player is.** At 55, a drop in the lagging category is a
couple of runs and excellent value. At 79 it is twenty drops per category for one point, and almost
anything else is a better evening. Never recommend watermark progression without saying which of those
two situations they are in. Community-guide figures, not official notes — rank actions by them, do not
quote them as exact.

Reconcile this against Combat Power rather than quoting whichever you saw first: headroom says where
raw power is, the watermark says what improves every future drop.

## Item level, Succession, and why traits survive

4.0.0 replaced Enhancement / Transfer / Sync with a unified **Item Level** system. Never mention
those three; they do not exist.

**Succession** (the UI still says Inheritance) transfers the item level of a **higher**-level piece
into the piece you use. The target **keeps its own traits and resonances**, which is the core of
gearing now: a high-level drop is *fuel* for gear you have already invested in, so **level and trait
investment are separable**.

- Moving **Potentials** consumes an **Inheritance / Succession Stone** — from the Resistance Supplies
  Merchant, or at low probability by melting equipment of **level 51+**.
- Each Potential transfer decrements an **inheritance count**. At 0 it cannot be inherited further —
  **but sealing the item resets the counter**, so this is a cost, not a dead end. Never tell a player
  a Potential can never move again.
- Succession cannot exceed the item's own maximum level.

## Traits — gear arrives with none, and only Heroic+ can have them

**"When you obtain equipment, it has no traits."** Traits are unlocked, not rolled, and **only
Heroic rarity and above** can have them unlocked or levelled. Rare and below had traits removed
entirely, so "improve this Rare piece's traits" is not a possible action.

- **3 traits per piece**, unlocked with **Trait Unlock Stones** (Unique gear needs Unique ones).
- Grown with **Trait Enhancement Stones**, which carry **their own item level** and only work on
  equipment of the **same or lower** level. Enhancement stones are therefore not fungible, and a
  level mismatch is a real, checkable blocker worth surfacing.
- **Unique traits** on Unique gear need trait-specific stones, not generic ones.

## Trait Resonance — four slots, sum of 40

Unlocking Resonance activates **four slots**, each upgradable to **10**, and the top bracket
(Level 4) needs the **slot total to reach 40**. Because the bracket keys on the sum, pushing one slot
to 10 while another sits at 2 achieves nothing — the same "laggard decides" shape as the watermark.
Uses **Attribute Resonance Stones**.

## Potential (Latent Abilities)

- Applied **randomly on acquisition**, and the **probability is visible in an unacquired item's
  tooltip** — worth asking the player to capture.
- They enhance skills or raise stats, and the skills inside them can themselves be levelled.
- **They survive sealing**, which is why sealed items carry a premium on the auction house.
- They can also **manifest when a Redfrost item is purified** — observed kinds are a weapon-mastery
  enhancement, +1 level to a skill, or a random stat such as Max Health.

## Skill Cores

- **60 Hero Skill Cores** for **Unique armour and accessories** (brooches excluded). Equipment skills
  were removed from those slots and replaced by core slots.
- Melting gives **8 Resin Cores**; **60 Resin Cores** craft a **Resin Flower**, which yields a chance
  at one of 60 cores. Weapon cores give no resin.
- **Tradable cores become untradable once equipped** — a one-way door worth warning about.
- Using cores **as materials** produces a result with **no Potential abilities**. A real trade-off.

## Runes — usually the largest single gap

**Level caps depend on grade and go to 120, not 60.** Community guides saying 20/40/60 are stale.

| Grade | Max level |
| --- | --- |
| 11 | 20 |
| 21 | 40 |
| 31 | 60 |
| 41 | 90 |
| 42 | **120** |

**A rune's stat is a weighted random roll, not a choice.** Each rune carries a pool of possible
stats with explicit probabilities summing to 100 — on a common weapon attack rune each crit stat is
about 8.4%, accuracy and double-attack about 6.3% each. So the correct framing is **"reroll until you
hit X"**, never "slot a rune for X".

**Four rune types: attack, defense, assist, and chaos.** A **Chaos rune counts as any type for
synergy purposes** — its pool spans all three archetypes — so it completes an ordered synergy from
any position while letting the player put the stat budget where they want. Chaos runes **do not
level**; they arrive at full value (a grade-31 chaos rune gives 400 where a maxed common attack rune
gives 100). The cost is the roll: about **4.8% per specific stat**, roughly 1-in-21.

**Synergies are ordered permutations and are gated at grade 41.** Every synergy requires grade 41
runes, so telling a player with common runes to "arrange them for the synergy" is wrong. Because the
combination is **ordered**, always state the sequence, not just the set.

Runes level by **consuming duplicates**, one level per duplicate, so rune progress is a volume
problem rather than a currency one. Unlocking a socket needs a **Rune Hammer**.

## Weapon Mastery — thresholds are cross-weapon

- **220 points maximum per weapon** (the ceiling has moved once already; re-check after patches).
- Three branches per weapon, and **branches are linear** — reaching an end-of-branch passive means
  buying everything before it, so price the whole path.
- **12 Mastery Skills** unlock two at a time at **130, 260, 390, 520, 650, 780**, with **4 slottable
  at once**; the four slots unlock at **130 / 260 / 390 / 520**.

**Those thresholds are totals across ALL weapons**, since 780 far exceeds one weapon's 220. So:

- Levelling a second or third weapon is **real progress**, not a distraction — even the second slot
  at 260 is unreachable on one weapon alone, and the top tier at 780 needs **at least four weapons**.
- A **secondary weapon passively earns 50%** of the active weapon's mastery XP.
- Past 520 no new slots arrive, so 650 and 780 grant skills to **choose between**, not accumulate.
- **Deactivating a passive costs 10,000 Sollant.**

## Artifacts

Six slots: **four Talistone, one Solarstone (active), one Lunarstone (passive)**. They come in
matched sets — roughly 14 of them, identifiable by a shared name prefix, so mixing across sets is a
real decision. Sources: Abyss Dungeons in Talandre, Nebula Island creatures, group dungeons.

**Riftstones are named after field bosses** ("Adentus Riftstone"), so tonight's boss determines which
riftstone is farmable tonight.

## Stellar Journey

Permanent stat increases via **Starry Memory**, consumed automatically on acquisition. Sources: the
Adventure and Exploration Codexes, **Traces of Spacetime**, Rift Rebellions and Relic Fishing. At
100% Traces of Spacetime, 2 Rift Rebellions and 1 Relic Fishing location appear daily.

It contributes to a stat's total but is **not redistributable** — it behaves like equipment.

## Redfrost and purification (Nix)

Redfrost items drop **only in Nix**, sealed and unusable, and are purified at **Shemir's Armillary
Sphere** to yield either equipment or **Embers of Shemir**, which craft Nix gear **with certainty**.
So the chain has a deterministic floor an ordinary drop never has.

**The binding constraint is Flame of Purification: 8,000 per week** from hunting and gathering. At
2,000 per Hero-grade purify that is **four Hero purifications a week** — this paces Redfrost far more
than drop rates do. Purify costs are Sollant **and** Flame:

| Grade | Sollant | Flame |
| --- | --- | --- |
| Advanced | 25,000 | 250 |
| Rare | 75,000 | 750 |
| Hero | 202,000 | 2,000 |
| Special | 404,000 | 3,000 |

Other hard limits: the Redfrost bag is **22 slots and cannot be expanded**; items are **lost** on
death in Nix, on leaving Nix by any means, and on logging out (only one safety slot survives, and
only within Nix); purification **interrupts if you take damage**.

**Unverified:** a player reports that *named* Redfrost items ("Redfrost Helmet of Nine Lives") yield
that specific item far more often with fewer embers than a generic "Redfrost Helmet", which can yield
any item of its slot. Official notes do not distinguish them. Do not quote different rates.

## Amitoi, morphs and food

- **38 Pal Synergies** grant permanent economy bonuses with known caps: **Sollant +9.1%, EXP +13%,
  Mastery +7%, Item Chance +8%**. A player below a cap has quantifiable headroom.
- **Morphs** (Glide, Aquatic, Dash) level up and grant traits — a real if minor progression track.
- **Food:** two combat buffs can stack, but **Attack and Defense cannot stack together** — valid
  pairs are Attack+Utility or Defense+Utility. **This restriction is UNVERIFIED against 4.5.0**; say
  it needs confirming rather than stating it flatly.

## Abyssal Contracts

**10 contracts per day at 50 tokens each** — a hard daily ceiling of 500. There is no way to go
faster, only a way to not miss days, and advice should say exactly that.
