# What people actually play, per class

Measured from **2337 questlog builds** — the most-liked of the last 30 days for every one
of the 45 classes, harvested per class so the rare ones are not crowded out by the popular ones.
Captured 6 August 2026. **4431 weapon slots** carried trait data and
**1135** carried heroic picks.

## Read this as a description, NOT as a target

These are the choices popular builds make. That is evidence about the meta and it is genuinely
useful — but it is **not** proof of what is optimal, and there are four specific reasons to keep it
at arm's length:

- **questlog is unmoderated.** Anyone can publish, likes measure visibility as much as quality, and
  a build can be half-finished or written for an older patch.
- **Popularity is self-reinforcing.** A build near the top gets copied because it is near the top.
- **The player's own build wins on intent.** If they pinned one, it says what they are trying to do.
  This table says what strangers did.
- **Never quote a percentage from here as a benchmark to reach.** "62% of Mystics take Dexterity"
  is a fact about a spreadsheet. "Your Dexterity is below the Mystic benchmark" is a number the
  player will act on, and it does not exist.

Use it to break ties, to sanity-check a stat spread that looks nothing like anyone else's, and to
answer "what do people usually do with this class" — which players ask constantly and which
nothing else in this prompt can answer.

## Sparse fields, and what could NOT be measured

Stated because absence here is a real finding rather than an omission:

- **Author tags are unusable.** Only 11% of builds carry any tag at all, and 12 classes have
  **zero** builds tagged PvE or PvP. So a class's PvE/PvP character cannot be read from tags, and
  anything claiming to is reading noise. **Ask the player their axis; do not infer it from a class.**
- **Potential abilities: 4-5% present.** Not measurable.
- **Trait Resonance: 30-44%.** Too patchy per class to characterise.
- Classes below the reporting threshold are marked so. A blank is honest; a number from four
  observations is not.

## The global baseline — the trap this table exists to avoid

Three weapon traits are near-universal, so naming them for a class says nothing:

| Trait | Share of all weapon slots |
| --- | --- |
| `all_double_attack` | 85.0% |
| `all_accuracy` | 70.0% |
| `all_critical_attack` | 64.3% |
| `attack_speed_modifier` | 25.8% |
| `hp_max` | 15.8% |
| `buff_given_duration_modifier` | 9.4% |

`all_double_attack`, `all_accuracy` and `all_critical_attack` sit on the majority of weapons in the
game. **A class is characterised by what it takes MORE than average**, which is what the lift
figures below measure — `3.4x` means that class takes it three and a half times as often as the
average class does. A trait at 1.0x is the meta, not the class.

Heroic base-stat baseline across all classes: **Perception 39%**, **Dexterity 19%**, **Fortitude 16%**, **Wisdom 16%**, **Strength 10%**.
Perception dominates everywhere, so Perception being a class's top pick is weak evidence; a class
leading on Fortitude or Wisdom is deviating and that means something.

## Per class

`stat priority` is the split of base-stat heroic picks — Strength, Dexterity, Wisdom, Perception,
Fortitude. `distinctive` lists weapon traits taken at least 1.6x more often than average.

### Bastion — Gauntlets + Sword and Shield

- **Stat priority:** Fortitude 41%, Strength 30%, Perception 17%, Wisdom 8%, Dexterity 5%  _(from 64 heroic picks)_
- **Distinctive traits:** `collide_amplification` 35% (5.6x), `buff_given_duration_modifier` 50% (5.3x), `hp_max` 53% (3.4x)

### Battleweaver — Crossbows + Staff

- **Stat priority:** not enough heroic picks observed to say.
- **Distinctive traits:** none — takes the same traits as the average class.

### Berserker — Sword and Shield + Daggers

- **Stat priority:** not enough heroic picks observed to say.
- **Distinctive traits:** `hp_max` 29% (1.8x)

### Brawler — Gauntlets + Daggers

- **Stat priority:** Wisdom 44%, Perception 31%, Dexterity 16%, Strength 5%, Fortitude 4%  _(from 55 heroic picks)_
- **Distinctive traits:** none — takes the same traits as the average class.

### Bulwark — Gauntlets + Orb

- **Stat priority:** Perception 57%, Dexterity 26%, Strength 9%, Fortitude 9%  _(from 23 heroic picks)_
- **Distinctive traits:** `collide_amplification` 21% (3.4x), `buff_given_duration_modifier` 16% (1.7x)

### Cavalier — Spear + Crossbows

- **Stat priority:** not enough heroic picks observed to say.
- **Distinctive traits:** none — takes the same traits as the average class.

### Channeler — Gauntlets + Staff

- **Stat priority:** Perception 53%, Dexterity 18%, Fortitude 18%, Strength 12%  _(from 17 heroic picks)_
- **Distinctive traits:** only 28 weapon slots observed — too few to characterise.

### Crucifix — Orb + Crossbows

- **Stat priority:** Perception 69%, Dexterity 20%, Strength 7%, Wisdom 3%  _(from 59 heroic picks)_
- **Distinctive traits:** `cost_consumption_modifier` 19% (3.7x), `buff_given_duration_modifier` 28% (3.0x), `attack_speed_modifier` 69% (2.7x), `skill_cooldown_modifier` 15% (2.0x)

### Crusader — Greatsword + Sword and Shield

- **Stat priority:** Fortitude 52%, Wisdom 18%, Perception 15%, Strength 10%, Dexterity 5%  _(from 40 heroic picks)_
- **Distinctive traits:** `collide_amplification` 39% (6.4x), `buff_given_duration_modifier` 42% (4.5x), `skill_cooldown_modifier` 17% (2.3x), `hp_max` 33% (2.1x)

### Darkblighter — Daggers + Wand and Tome

- **Stat priority:** not enough heroic picks observed to say.
- **Distinctive traits:** `weaken_accuracy` 9% (4.8x), `skill_cooldown_modifier` 12% (1.7x)

### Disciple — Sword and Shield + Staff

- **Stat priority:** not enough heroic picks observed to say.
- **Distinctive traits:** `cost_max` 6% (5.5x), `cost_regen` 8% (4.8x), `hp_max` 33% (2.1x)

### Enigma — Orb + Staff

- **Stat priority:** Perception 49%, Dexterity 41%, Strength 8%, Fortitude 3%  _(from 39 heroic picks)_
- **Distinctive traits:** `cost_regen` 9% (5.7x), `skill_cooldown_modifier` 14% (1.9x)

### Eradicator — Spear + Staff

- **Stat priority:** not enough heroic picks observed to say.
- **Distinctive traits:** none — takes the same traits as the average class.

### Fury — Crossbows + Wand and Tome

- **Stat priority:** Perception 77%, Strength 8%, Dexterity 8%, Wisdom 8%  _(from 13 heroic picks)_
- **Distinctive traits:** `weaken_accuracy` 7% (3.3x)

### Gladiator — Spear + Greatsword

- **Stat priority:** Dexterity 31%, Perception 31%, Wisdom 23%, Fortitude 15%  _(from 39 heroic picks)_
- **Distinctive traits:** `attack_speed_modifier` 63% (2.4x)

### Guardian — Orb + Sword and Shield

- **Stat priority:** Fortitude 44%, Strength 25%, Perception 15%, Wisdom 12%, Dexterity 4%  _(from 48 heroic picks)_
- **Distinctive traits:** `collide_amplification` 41% (6.6x), `cost_consumption_modifier` 20% (3.8x), `buff_given_duration_modifier` 32% (3.4x), `hp_max` 44% (2.8x)

### Impaler — Spear + Longbow

- **Stat priority:** not enough heroic picks observed to say.
- **Distinctive traits:** none — takes the same traits as the average class.

### Infiltrator — Longbow + Daggers

- **Stat priority:** Perception 76%, Wisdom 24%  _(from 25 heroic picks)_
- **Distinctive traits:** `attack_speed_modifier` 46% (1.8x)

### Invocator — Staff + Wand and Tome

- **Stat priority:** Dexterity 45%, Perception 41%, Strength 11%, Wisdom 2%  _(from 44 heroic picks)_
- **Distinctive traits:** `cost_regen` 15% (9.4x), `skill_cooldown_modifier` 19% (2.5x)

### Juggernaut — Gauntlets + Greatsword

- **Stat priority:** Dexterity 33%, Perception 33%, Wisdom 19%, Fortitude 15%  _(from 52 heroic picks)_
- **Distinctive traits:** `attack_speed_modifier` 53% (2.0x)

### Justicar — Orb + Greatsword

- **Stat priority:** Perception 46%, Dexterity 19%, Fortitude 19%, Wisdom 11%, Strength 5%  _(from 37 heroic picks)_
- **Distinctive traits:** `skill_cooldown_modifier` 14% (1.9x), `attack_speed_modifier` 47% (1.8x)

### Liberator — Longbow + Staff

- **Stat priority:** Perception 46%, Dexterity 23%, Wisdom 15%, Strength 15%  _(from 13 heroic picks)_
- **Distinctive traits:** none — takes the same traits as the average class.

### Lunarch — Orb + Daggers

- **Stat priority:** Perception 58%, Dexterity 22%, Strength 17%, Wisdom 3%  _(from 36 heroic picks)_
- **Distinctive traits:** none — takes the same traits as the average class.

### Marauder — Gauntlets + Crossbows

- **Stat priority:** Wisdom 43%, Perception 33%, Fortitude 10%, Dexterity 10%, Strength 5%  _(from 21 heroic picks)_
- **Distinctive traits:** `buff_given_duration_modifier` 24% (2.5x), `skill_cooldown_modifier` 18% (2.4x), `collide_amplification` 14% (2.2x)

### Mystic — Gauntlets + Wand and Tome

- **Stat priority:** Dexterity 64%, Perception 21%, Wisdom 11%, Strength 5%  _(from 66 heroic picks)_
- **Distinctive traits:** `buff_given_duration_modifier` 28% (3.0x), `skill_cooldown_modifier` 15% (2.0x), `attack_speed_modifier` 48% (1.8x)

### Oracle — Orb + Wand and Tome

- **Stat priority:** Perception 50%, Fortitude 37%, Strength 7%, Dexterity 6%  _(from 68 heroic picks)_
- **Distinctive traits:** `cost_consumption_modifier` 75% (14.5x), `attack_speed_modifier` 46% (1.8x), `hp_max` 25% (1.6x)

### Outrider — Crossbows + Greatsword

- **Stat priority:** not enough heroic picks observed to say.
- **Distinctive traits:** none — takes the same traits as the average class.

### Paladin — Greatsword + Wand and Tome

- **Stat priority:** Perception 50%, Dexterity 25%, Strength 17%, Wisdom 8%  _(from 12 heroic picks)_
- **Distinctive traits:** none — takes the same traits as the average class.

### Polaris — Orb + Spear

- **Stat priority:** Dexterity 44%, Wisdom 22%, Perception 20%, Fortitude 11%, Strength 2%  _(from 45 heroic picks)_
- **Distinctive traits:** `attack_speed_modifier` 43% (1.7x)

### Raider — Crossbows + Sword and Shield

- **Stat priority:** Perception 42%, Fortitude 25%, Wisdom 17%, Strength 17%  _(from 12 heroic picks)_
- **Distinctive traits:** `collide_amplification` 33% (5.3x)

### Ranger — Longbow + Greatsword

- **Stat priority:** Perception 46%, Fortitude 23%, Wisdom 15%, Dexterity 8%, Strength 8%  _(from 13 heroic picks)_
- **Distinctive traits:** none — takes the same traits as the average class.

### Ravager — Greatsword + Daggers

- **Stat priority:** Wisdom 45%, Perception 27%, Dexterity 18%, Strength 9%  _(from 22 heroic picks)_
- **Distinctive traits:** `attack_speed_modifier` 48% (1.8x)

### Scorpion — Crossbows + Daggers

- **Stat priority:** Wisdom 73%, Perception 27%  _(from 15 heroic picks)_
- **Distinctive traits:** none — takes the same traits as the average class.

### Scout — Longbow + Crossbows

- **Stat priority:** not enough heroic picks observed to say.
- **Distinctive traits:** `weaken_accuracy` 8% (4.1x)

### Scryer — Orb + Longbow

- **Stat priority:** Perception 49%, Fortitude 20%, Dexterity 14%, Strength 14%, Wisdom 4%  _(from 51 heroic picks)_
- **Distinctive traits:** `debuff_taken_duration_modifier` 9% (9.7x), `cost_consumption_modifier` 25% (4.7x), `skill_cooldown_modifier` 24% (3.2x), `attack_speed_modifier` 51% (2.0x), `buff_given_duration_modifier` 19% (2.0x)

### Seeker — Longbow + Wand and Tome

- **Stat priority:** Perception 51%, Fortitude 23%, Wisdom 19%, Strength 6%  _(from 47 heroic picks)_
- **Distinctive traits:** `debuff_taken_duration_modifier` 6% (6.3x), `cost_consumption_modifier` 22% (4.3x), `skill_cooldown_modifier` 21% (2.9x), `hp_max` 36% (2.3x), `attack_speed_modifier` 42% (1.6x)

### Sentinel — Staff + Greatsword

- **Stat priority:** not enough heroic picks observed to say.
- **Distinctive traits:** `cost_regen` 6% (3.7x)

### Shadowdancer — Spear + Daggers

- **Stat priority:** Wisdom 48%, Dexterity 30%, Perception 22%  _(from 23 heroic picks)_
- **Distinctive traits:** none — takes the same traits as the average class.

### Skirmisher — Gauntlets + Spear

- **Stat priority:** Perception 43%, Fortitude 25%, Dexterity 22%, Wisdom 8%, Strength 2%  _(from 51 heroic picks)_
- **Distinctive traits:** `collide_amplification` 24% (3.8x)

### Spellblade — Staff + Daggers

- **Stat priority:** not enough heroic picks observed to say.
- **Distinctive traits:** none — takes the same traits as the average class.

### Steelheart — Spear + Sword and Shield

- **Stat priority:** Fortitude 56%, Strength 25%, Perception 12%, Wisdom 6%  _(from 16 heroic picks)_
- **Distinctive traits:** `collide_amplification` 24% (3.9x), `hp_max` 33% (2.1x)

### Strider — Gauntlets + Longbow

- **Stat priority:** Perception 44%, Wisdom 41%, Fortitude 8%, Strength 5%, Dexterity 3%  _(from 66 heroic picks)_
- **Distinctive traits:** `cost_max` 9% (8.7x), `attack_speed_modifier` 47% (1.8x)

### Templar — Sword and Shield + Wand and Tome

- **Stat priority:** Fortitude 47%, Perception 35%, Strength 12%, Wisdom 6%  _(from 17 heroic picks)_
- **Distinctive traits:** `weaken_accuracy` 10% (4.9x), `hp_max` 51% (3.2x)

### Voidlance — Spear + Wand and Tome

- **Stat priority:** not enough heroic picks observed to say.
- **Distinctive traits:** none — takes the same traits as the average class.

### Warden — Sword and Shield + Longbow

- **Stat priority:** not enough heroic picks observed to say.
- **Distinctive traits:** `hp_max` 60% (3.8x), `collide_amplification` 10% (1.6x)

## How to use a profile in an answer

Good: "Mystic builds lean hard on Dexterity — 64% of their heroic picks, against 19% across all
classes. Your spread is Fortitude-heavy, which is unusual for this class; if that is deliberate,
keep it, and if it is not, Dexterity is where the class's damage comes from."

Bad: "Mystics need 64% Dexterity." That is not what the number means and it is not achievable
advice — it is a share of trait picks across strangers' builds, not a stat target.

And when a class shows nothing distinctive, **say so plainly**. "Nothing unusual about this class's
trait choices" is a real answer; inventing a characterisation to fill the gap is not.
