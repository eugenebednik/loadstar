---
name: capture-boss-schedule
description: Read the in-game boss schedule from screenshots of the map's Daily tab and update docs/boss-schedule.json. Use when the rotation has changed after a patch or weekly maintenance, or when the bundled schedule has gone stale.
---

# Capture the boss schedule

Turn screenshots of the player's own client into `docs/boss-schedule.json`. That file is served by
GitHub Pages and fetched by every install, so one commit updates every user — no release, no
reinstall.

The client is the only authority here. questlog's Americas grid says 19:00/22:00; the client says
17:00/20:00, and the client is right for the player's own server. Never reconcile toward an external
source.

## Rule zero: you drive nothing

**Never click, type, or scroll inside the game.** Take screenshots and read them; the player operates
the client.

This is not squeamishness. EasyAntiCheat is running on a live account, and synthetic input into the
client is the pattern that gets accounts flagged — it does not matter that an assistant rather than
the app is generating it. Loadstar's entire posture is that input never reaches the game
(`docs/anti-cheat-posture.md`), and this skill does not get an exemption.

So every step below is: **ask the player to do something, then screenshot.**

## Step 1 — get the right view

Ask the player to open the **map → Daily tab**, and to press the button at the top-right of the
schedule panel (tooltip: *"Zoom in the timetable"*) so icons are **minimized**.

Insist on the minimized view. It is not cosmetic:

| | Zoomed in | Minimized |
| --- | --- | --- |
| Days visible | ~9 | ~18 |
| Time labels | centred under an icon *group* | **explicit text, adjacent to its icons** |
| Horizontal overflow | `>` chevrons — events cut off | none |

In the zoomed view a time floats under a cluster and which icons belong to it is inferential. Reading
it produces confident, wrong groupings. In the minimized view each row reads `17:00 [icons] 20:00
[icons]` in sequence.

If a screenshot shows the zoomed view, say so and ask for the toggle rather than reading it anyway.

## Step 2 — capture until the scrollbar bottoms out

The panel scrolls. One screenshot is roughly 18 of about 21 days.

Loop: screenshot → note the last date → ask the player to scroll down → screenshot again. Stop when
the last row repeats or the scrollbar thumb is at the bottom. **A partial capture is not a schedule**
— committing one silently deletes the days you did not see.

Also read the panel's own header, which shows the current date and time (e.g. `05/08 Wed 15:31`).
Cross-check it against the machine clock: they should match, because the panel is labelled **Local
Time** at the bottom left. If they diverge, stop and report it — the whole timezone model assumes
they agree.

## Step 3 — read each row

Per date: the day, and for each time slot the time, the event type, and how many icons.

- Empty days are real and must be recorded as empty. Thursday and Monday carry nothing.
- **Siege is not a field boss.** It is a single orange shield icon at a time no other day uses
  (18:00 observed), it is scheduled guild PvP, and labelling it `FieldBosses` sends players to a boss
  that does not exist. Type it `Siege`.
- Icon **counts** vary per slot and rotate week to week (one boss at 17:00 and five at 20:00 one week,
  inverted the next). Times do not rotate. Record what you see.

## Step 4 — find the recurrence before writing weekday slots

`weeklySlots` repeats every week. Anything that does not repeat weekly must carry `everyDays` and a
`since` anchor, or it will be wrong on the off weeks.

Compare the same weekday across the captured range:

- Same times every occurrence → plain weekly slot.
- Present, absent, present → biweekly. Set `everyDays: 14` and `since` to the **first observed
  occurrence** as `YYYY-MM-DD`.

This is not hypothetical: siege was observed on 09/08 and 23/08 with **16/08 empty**. A weekday-only
slot promised a siege every Sunday and was wrong every other week.

If a pattern needs a period longer than 16 days, raise the day-walk bound in
`BossSchedule.NextSpawns` to exceed it — otherwise the next occurrence falls outside the search
window and the slot silently yields nothing.

## Step 5 — boss names, only when certain

Each slot takes an optional `bosses` array. A slot holds several bosses at one time, so it is a list:

```jsonc
{ "time": "20:00", "type": "FieldBosses", "bosses": ["Ahzreil", "Talus", "Grand Aelon"] }
```

That renders as `20:00 - Ahzreil, Talus, Grand Aelon`. Absent or empty renders `Field Bosses`.

### The hover tooltip is CONFIRMED, and it gives more than the name

Verified on a live client 2026-08-05. Hovering a schedule icon produces, for example:

```
[Peace] Ramux
Stillreach | Monsters Lv. 60
```

So a direct text read is available and icon matching is unnecessary. Three fields come free, and
**all three matter**:

- **The name.**
- **The zone** (`Stillreach`). "Ramux — Stillreach" is a materially better countdown than "Ramux",
  because the player has to travel there.
- **The bracketed CONTEST MODE, which is not decoration.** `[Peace]` is open to anyone. `[Guild]` is
  a guild-only PvP contest — guilds competing against each other for the kill.

`[Guild]` changes the advice, not just the label:

1. **A solo or guildless player cannot participate at all.** Counting one down for them, or
   recommending they travel, wastes their evening. This is the single most useful thing the mode tag
   buys.
2. **It flips the gear axis.** PvP and PvE are separate stat investments in this game — accuracy
   versus crit, endurance versus evasion. Preparation advice for a `[Guild]` boss is PvP advice.

- **A despawn window**, on some entries: `Despawns after 50min.` That is how long the player has to
  actually get there, which is worth more than the spawn instant alone.

#### Mode belongs to the OCCURRENCE, not the boss — corrected 2026-08-05

Two tooltips, same boss, same weekday, same time, different weeks:

| Date | Tooltip |
| --- | --- |
| 05/08 Wed 20:00 | `[Guild] Ramux` — Stillreach, Lv. 60, *Despawns after 50min* |
| 12/08 Wed 20:00 | `[Peace] Ramux` — Stillreach, Lv. 60 |

So Ramux is guild-contested one Wednesday and open the next. **Do not treat mode as an attribute of
the boss, and do not copy the mode you observed today onto every future occurrence of that weekday** —
that is exactly the mistake the weekly model invites, and it would tell a guildless player to skip a
boss that is open to them.

It also means mode may run on a cycle of its own, the same shape as the biweekly siege. Capture enough
weeks to see whether it alternates before encoding it. If it does, the mode needs the same
`everyDays`/`since` treatment the slots have, or it needs to live on dated entries rather than weekday
ones.

#### Mode is readable from the icon overlay — no hover required

Reported by the product owner 2026-08-05, and it resolves the problem above:

- **A shield over a boss icon means PvP** — the `[Guild]` contest.
- **A dove over a boss icon means peace** — open to anyone.

So mode does not need the tooltip at all. Read it off the row, per icon, for every occurrence in the
capture. That is what makes recording it safe: instead of inferring a cycle from two data points, the
capture observes the mode of all ~21 days directly, and any cycle falls out of the observations rather
than being assumed.

This also corrects an earlier misreading in this project's notes. The first pass over the zoomed view
described some icons as carrying a "green shield badge" and treated it as part of the boss artwork. It
was the mode overlay. If a description of an icon mentions a shield, that is mode, not art.

Practically this is the easiest thing in the whole capture: shield versus dove is a far coarser visual
difference than one boss silhouette against another, and it survives the resolution the schedule
renders at. Names still need the hover; mode does not.

Until the overlays have actually been read, **omitting mode is correct**. A name with no mode is
incomplete; a name with the wrong mode is misleading.

**Archbosses appear inside field-boss slots.** Ramux is an archboss and was found among the icons of a
`FieldBosses` 20:00 slot, so the slot `type` alone does not describe what is spawning.

### The current shape cannot hold this

`bosses` is a flat array of strings, which loses the mode, the zone and the archboss distinction —
exactly the three things worth having. **Widen the model BEFORE capturing**, or the capture has to be
redone:

```jsonc
"bosses": [
  // mode read from the icon overlay: shield = pvp, dove = peace
  { "name": "Ramux", "mode": "pvp", "zone": "Stillreach", "kind": "archboss", "despawnMinutes": 50 },
  { "name": "Talus", "mode": "peace", "zone": "Urstella Fields" }
]
```

Keep the plain-string form parsing as a name-only entry, so the existing data and tests stay valid.
`BossSpawn.DisplayName` joins the names; the overlay should mark `guild` entries visibly, since that
is the one a player must not travel to uninvited.

**Leave it empty unless you actually read the name.** A plausible wrong boss is indistinguishable
from a right one until the player arrives somewhere empty, and it is worse than no name at all.
Schedule icons are unlabelled, so getting names means one of these, in order of reliability:

1. **Hover a schedule icon** and screenshot the tooltip, if it names the boss. Direct text read.
2. **Content Settings toggles** — untick all but one boss and see which icons disappear.
   Identification by elimination, and it cannot be wrong.
3. **The legend** (Content Settings lists every boss beside its icon in text) matched against the
   schedule icons by eye. Last resort: zoom both to high magnification and compare side by side, a
   few slots at a time. Fill in only what is unambiguous.

Note that a boss the player has **unticked in Content Settings does not appear on their schedule at
all**, so a capture reflects their filters, not the game's full roster.

## Step 6 — write, validate, verify

Edit `docs/boss-schedule.json`. Do not create a second copy anywhere: the assembly embeds this exact
file as the offline fallback (see the `EmbeddedResource` in
`src/Loadstar.Games.ThroneAndLiberty/Loadstar.Games.ThroneAndLiberty.csproj`), so a duplicate would
drift and leave the schedule current in one place and stale in the other.

Record in the file's `$source` array **what was observed, when, and from which client build** — the
existing entries are the model. That provenance is what lets a future session tell a measurement from
a guess.

Then, in order:

1. **Show the player the parsed table** — dates, times, types, counts — and get confirmation before
   committing. The value of this whole exercise is that their client is authoritative, which is
   wasted if a misread screenshot becomes the new truth unchallenged.
2. `python -c "import json;json.load(open('docs/boss-schedule.json'))"` — it must parse.
3. `dotnet test` — `BossScheduleTests` covers the biweekly skip, weekly fallback, and the bundled
   Americas data. If a test now contradicts the capture, decide which is right before changing either;
   a test asserting last month's rotation should be updated, but a test failing because the JSON is
   malformed should not be.
4. Commit and push. Pages redeploys in a minute or two; confirm with
   `curl -s https://eugenebednik.github.io/loadstar/boss-schedule.json | head -5` before telling the
   player it is live.

## Shape reference

```jsonc
"regions": {
  "Americas": {
    "defaultTimeZone": "America/Los_Angeles",   // a default, not a fact; slot times are read in
                                                // the player's own zone
    "weeklySlots": {
      "Monday": [],                             // empty days are meaningful
      "Wednesday": [
        { "time": "17:00", "type": "FieldBosses", "bosses": [] },
        { "time": "20:00", "type": "FieldBosses", "bosses": [] }
      ],
      "Sunday": [
        { "time": "18:00", "type": "Siege", "everyDays": 14, "since": "2026-08-09" }
      ]
    }
  }
}
```

`everyDays` defaults to 7 and `bosses` to empty, so an unadorned slot stays a plain weekly one.

`dailySlots` is the older flat form, kept only so the difference from questlog stays visible. It
cannot express an empty day. Never add to it.
