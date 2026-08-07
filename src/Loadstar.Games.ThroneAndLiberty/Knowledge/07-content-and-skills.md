# Content, skills, and where gear actually comes from

The rest of this pack explains how systems *work*. This section is about **where the inputs come
from** — which activity yields the material a recommendation depends on. Advice that names a cost
without naming a source is only half an answer: "you need 3 Trait Resonance Stones" is not actionable
until the player knows what to run tonight.

## Skill Specialization — separate from stats, and there are two late windfalls

Skills are upgraded with **Skill Specialization Points**, which are a different currency from the base
stat points and are **not** part of the redistributable stat pool.

| When | Points |
| --- | --- |
| Level 15 | 10 to start |
| Levels 15 → 30 | 2 per level |
| Levels 30 → 50 | 3 per level |
| **Level 56** | **+5 in one go** |
| **Level 60** | **+5 in one go** |

Points modify a skill: more damage, more range, or a change to how it functions. They are respecced
separately from stats.

**The two windfalls matter for advice.** A player who levelled past 56 or 60 without spending them is
carrying ten unspent points, which is free power sitting idle — the same shape as an empty artifact
slot. Worth asking about whenever someone at level 56+ asks what to do next, because nothing on the
character sheet shows unspent specialization points.

## The content map — what to run for what

| Activity | Yields | Notes |
| --- | --- | --- |
| **Dimensional Trials** | Gear, accessories | Co-op dungeon scored on accumulated points; rewards scale with the total. Gated by Dimensional Contract Tokens, which are finite per week. |
| **Guild Raids** | Guild Coins, gear | **Guild Level 30** required. Ascended raids include Daigon, Leviathan, Pakilo Naru and Manticus. |
| **Abyss Dungeons** (Talandre) | Artifacts, by type, from a chest | The artifact route. |
| **Archbosses** | Archboss weapons at fixed **item level 85** | Ramux weapons craft from **Thundercloud Scales** or **Skill Cores**. |
| **Nix** | Redfrost items, Embers of Shemir | The purification chain — see the Redfrost section. Weekly Flame cap is the real limit. |
| **Resistance Contracts** | Abyssal Contract Tokens | 10/day at 50 each, so a hard 500/day ceiling. |
| **Field bosses** | Riftstones named after them | Tonight's boss decides which riftstone is farmable — see the boss schedule. |
| **Battlegrounds** | PvP rewards | "Giant's Cityscape" is 48 players, two teams of 24. |
| **Arena** | PvP rewards | **Equalized** — gear is normalised, so arena performance says nothing about gear progress and gear advice does not help it. Modes are 2v2, 3v3, 6v6. |

**The equalized arena is worth knowing before giving advice.** If a player says they are losing in
arena, gear recommendations are the wrong answer entirely — that content flattens gear on purpose.

## Event modes — the vocabulary the boss schedule uses

| Mode | Meaning |
| --- | --- |
| **Peace** | Shared objective, players cannot fight each other. Open to anyone. |
| **Conflict** | Same objective, players CAN fight each other. Marked with crossed red swords. |
| **Guild** | Guild-only contest; rewards by guild performance. A guildless player cannot take part. |
| **Dominion** | Randomly assigned teams competing for objectives. |

Most Archboss events are Peace. This is the same vocabulary the schedule's `mode` field carries, and
it decides two things: whether the player can participate at all, and whether preparation advice
should be on the PvP or the PvE stat axis.

## Raising item level — the mechanism, with a warning about sources

Item level is raised with **Growthstones**, matched to the item's category (weapon / armour /
accessory) and to its rarity. The first upgrade costs one stone and each level after that costs one
more, so the curve steepens quickly. Sollant is charged alongside.

**TREAT PUBLISHED GROWTHSTONE GUIDES WITH SUSPICION.** Almost all of them describe "Equipment
Enchanting" reached with the `.` key, which is the **pre-4.0.0 Enhancement system that was removed**.
Live API data does still carry an `itemEnchant` table with per-level `requiredGold` and named
Growthstone quantities, so stones of this kind clearly still exist — but the surrounding flow those
guides describe does not, and the exact current interface is **UNCONFIRMED**. Quote the costs from
the API when they are supplied to you; do not describe the UI steps.

## Searched and NOT found

Recorded so the same ground is not covered twice, and so absence is not mistaken for an oversight:

- **Vitality / rested-XP.** Searched 7 August 2026; no such system surfaced for this game. If a player
  mentions one, ask rather than assuming it maps to something here.
- **Talents, Wisps, Glider progression.** No evidence these are TL systems under those names. Morphs
  (Glide, Aquatic, Dash) are the travel system and they do level — see the buffs section.
- **Seasons / leaderboards.** PvP seasons exist as a concept in patch commentary but nothing found
  that connects them to gear progression, which is the only reason this pack would carry them.

## What is still missing from this pack

Named so the gap is visible rather than implied. None of these blocks advice today, but each would
improve it, and none should be guessed at:

- **Growthstone acquisition** — where the stones come from per category, and the realistic rate.
- **Dimensional Trial tiers** — the star ratings and what each yields.
- **Guild progression** — how a guild reaches level 30, since two raids are gated on it.
- **Codex completion** — Adventure and Exploration Codex feed Stellar Journey; the specific rewards
  are not captured.
- **Boonstones** — six exist and are named, but their mechanics are not covered here.
