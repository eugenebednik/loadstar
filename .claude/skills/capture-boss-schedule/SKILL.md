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
