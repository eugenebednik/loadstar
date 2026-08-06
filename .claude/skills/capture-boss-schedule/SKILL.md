---
name: capture-boss-schedule
description: Read the in-game boss schedule from screenshots of the map's Hourly and Daily tabs, update docs/boss-schedule.json, and publish it. Use when the rotation has changed after a patch or weekly maintenance, when a monthly event needs new dates, or when the bundled schedule has gone stale.
---

# Capture the boss schedule

Turn screenshots of the player's own client into `docs/boss-schedule.json`, then publish it. That
file is served by GitHub Pages and fetched by every install, so **one commit updates every user** —
no release, no reinstall.

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

## The three streams, and which tab each comes from

The game runs several schedules concurrently. Putting a capture in the wrong stream is the most
consequential mistake available here, so decide this first.

| JSON key | Game source | Recurrence | Contents |
| --- | --- | --- | --- |
| `hourlySlots` | **Hourly** tab | every day | Regular field bosses. 7 slots. Composition rotates. |
| `weeklySlots` | **Daily** tab | per weekday | Siege and archbosses. Days can be empty. |
| `datedSlots` | either | explicit dates | Monthly events, raids, tax delivery, dated archboss composition. |

`hourlySlots` and `datedSlots` are **additive** — they stack on top of `weeklySlots`, and a day
genuinely carries all of them at once. `dailySlots` is a **fallback** for `weeklySlots` and is
legacy; never add to it.

A day with archbosses shows both streams. Before they were merged, a weekday the Daily tab leaves
empty produced a countdown to something two days out while seven field bosses were spawning that
evening.

## Step 1 — get the right view

Ask the player to open the **map**, and to press the button at the top-right of the schedule panel
(tooltip: *"Zoom in the timetable"*) so icons are **minimized**. Then capture **both tabs**.

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

Also read the panel's own header, which shows the current date and time (e.g. `05/08 Wed 15:31`).
Cross-check it against the machine clock: they should match, because the panel is labelled **Local
Time** at the bottom left. If they diverge, stop and report it — the whole timezone model assumes
they agree.

## Step 2 — capture until the scrollbar bottoms out

The panel scrolls. One screenshot is roughly 18 of about 21 days.

Loop: screenshot → note the last date → ask the player to scroll down → screenshot again. Stop when
the last row repeats or the scrollbar thumb is at the bottom. **A partial capture is not a schedule**
— committing one silently deletes the days you did not see.

The Hourly tab also scrolls, and its list has a top: nothing above 08:00 and nothing after 23:00
Pacific. An empty stretch is a real observation, so record that you reached the end rather than
assuming the list continues.

## Step 3 — read each row

Per date: the day, and for each time slot the time, the event type, and how many icons.

- Empty days are real and must be recorded as `[]`, not omitted. Thursday and Monday carry nothing on
  the Daily tab. Omitting a day and writing `[]` parse the same but *say* different things — `[]` is
  "confirmed nothing", absent is "nobody looked", and the validator warns about absent ones.
- **Siege is not a field boss.** It is a single orange shield icon at a time no other day uses
  (18:00 Pacific observed), it is scheduled guild PvP, and labelling it `FieldBosses` sends players
  to a boss that does not exist. Type it `Siege`.
- **The Daily tab's bosses are `ArchBosses`, not `FieldBosses`.** That tab carries only siege and
  archbosses. Typing them as field bosses is wrong on its own terms, and it also makes the merged
  overlay show two identical-looking rows with no way to tell which is worth organising for.
- Icon **counts** vary per slot and rotate week to week (one boss at 17:00 and five at 20:00 one week,
  inverted the next). Times do not rotate. Record what you see.

### Event types are open

`GenericName` splits PascalCase, so `TaxDelivery` renders "Tax Delivery" and `GuildRaid` renders
"Guild Raid" **with no code change**. That is deliberate: the schedule is published on the game's
cadence, and needing a release to make a new type readable would defeat the point.

Two rules: name types plainly in PascalCase, and avoid runs of capitals — `PvPEvent` renders
"Pv PEvent", because casing alone cannot say where the word breaks.

## Step 4 — pick the right recurrence

- Same times every occurrence, every day → `hourlySlots`.
- Same times on a given weekday, every week → `weeklySlots` under that weekday.
- Anything else → `datedSlots`, with an explicit `dates` list.

**Prefer `datedSlots` whenever you are not certain.** A dated entry is right or absent; a weekly entry
that should not have been weekly is confidently wrong every off week. There is precedent: an earlier
pass read one Sunday as empty, concluded siege was biweekly, built `everyDays`/`since` for it, shipped
it, and was corrected — siege is weekly. Nothing in the file uses that machinery now.

When a `dates` list runs out the event stops appearing. **That is the intended behaviour**, not a bug
to paper over: the in-game panel only shows about three weeks, so a longer list is extrapolation, and
a wrong date on a PvP event is a guild turning up to nothing.

### Monthly events

Vienta tax delivery runs 17:30 Pacific on the last Sunday of the month. **Do not try to express that
as a rule.** Slot times are UTC and the weekday rolls: 17:30 Pacific Sunday is 00:30 UTC the
*following Monday*, which is the last Monday in most months and the **first Monday of the next
month** when that Sunday falls on the 31st. A rule that is right eleven times a year is worse than a
list, because the twelfth failure is silent.

List the dates. Refresh them when you capture.

## Step 5 — times are UTC, and the weekday moves

`"timeBasis": "utc"`. Slot times are UTC and weekday keys are UTC weekdays, because a boss spawns at
one instant that every player sees in their own zone. Reading a wall clock in the *caller's* zone made
the countdown correct only for players whose machine matched the server — an Eastern player on an
Americas server got a countdown three hours early that looked entirely plausible.

Converting from the Pacific times the client shows:

```
Pacific + 7h = UTC   (PDT, Mar-Nov)        Pacific + 8h = UTC   (PST, Nov-Mar)
```

**Anything at or after 17:00 Pacific rolls to the next UTC day.** 17:00 Pacific Friday is 00:00 UTC
Saturday, and Sunday's siege lands on Monday.

Two things make this survivable:

- **Put `localPst` on every slot.** It is the Pacific time the client showed, kept so the file stays
  checkable against the game without doing arithmetic in your head.
- **Run the validator**, which recomputes every conversion from `localPst` and fails on a mismatch.

For `hourlySlots` the day roll is a **no-op** — the table applies to every UTC day, so the union
covers every Pacific day's seven exactly once. For `weeklySlots` and `datedSlots` it genuinely moves
events, and that is where errors hide.

## Step 6 — boss names, only when certain

Each slot takes an optional `bosses` array. A slot holds several bosses at one time, so it is a list,
and entries may be bare strings or objects:

```jsonc
"bosses": [
  { "name": "Ramux", "mode": "pvp", "zone": "Stillreach", "kind": "archboss", "despawnMinutes": 50 },
  "Talus"
]
```

**`icon-legend.json` in this directory is the closed vocabulary.** It lists every name the client is
known to use — 38 bosses, 9 archbosses, 5 of 6 boonstones — read from the map's Content Settings
window, which prints every boss beside its icon *in text*. The validator rejects any name not in it.

So the workflow is: a candidate name goes into the legend only after it has been read off the client,
and into the schedule only after it is in the legend. **Leave `bosses` empty unless you actually read
the name.** A plausible wrong boss is indistinguishable from a right one until the player arrives
somewhere empty, and it is worse than no name at all.

Adding to the legend, in order of reliability:

1. **Hover a schedule icon** and screenshot the tooltip. Direct text read, and it gives more than the
   name (below).
2. **Content Settings** — the full roster with names in text. One capture completes the legend.
3. **Content Settings toggles** — untick all but one boss and see which icons disappear.
   Identification by elimination, and it cannot be wrong.

A boss the player has **unticked in Content Settings does not appear on their schedule at all**, so a
capture reflects their filters, not the game's roster. An absent boss may be filtered rather than
unscheduled.

### The hover tooltip gives four things

Verified on a live client 2026-08-05:

```
[Peace] Ramux
Stillreach | Monsters Lv. 60
Despawns after 50min.
```

- **The name.**
- **The zone** (`Stillreach`) — the player has to travel there, so "Ramux — Stillreach" is a
  materially better countdown than "Ramux". Record it in the legend; it is per-boss and stable.
- **The despawn window** — how long they have to actually arrive, which the spawn instant alone does
  not give.
- **The bracketed contest mode**, which is the one that is *not* stable. See below.

## Step 7 — contest mode, which is per-occurrence

`[Guild]` is a guild-only PvP contest; `[Peace]` is open to anyone. This changes the advice, not just
the label:

1. **A solo or guildless player cannot participate at all.** Counting one down for them, or
   recommending they travel, wastes their evening.
2. **It flips the gear axis.** PvP and PvE are separate stat investments in this game — accuracy
   versus crit, endurance versus evasion.

**Mode belongs to the occurrence, not the boss.** Two tooltips, same boss, same weekday, same time,
adjacent weeks:

| Date | Tooltip |
| --- | --- |
| 05/08 Wed 20:00 | `[Guild] Ramux` — Stillreach, Lv. 60, *Despawns after 50min* |
| 12/08 Wed 20:00 | `[Peace] Ramux` — Stillreach, Lv. 60 |

So **never copy the mode you observed today onto every future occurrence**, and never record it in
`icon-legend.json`. That file deliberately has no `mode` field, and says why.

Where mode actually comes from:

- **The hourly stream: from the SLOT TIME.** 18:30 and 21:30 Pacific are guild every day; the other
  five slots are peace. Already encoded as `"mode": "guild"` on those two slots. This needs no
  per-boss data and no hovering at all.
- **Archbosses and dated events: from the tooltip**, per occurrence, onto a dated slot.
- **Siege and tax delivery: always PvP.** Both carry `"mode": "guild"`.

`mode` may sit on a **slot** as well as on a boss. That is what lets a guild slot render "Guild Boss"
when its occupant rotates and is unnamed — the mode is known even though the boss is not.

### Mode is NOT readable from a screenshot — tested, and it failed

The client draws a badge in the **bottom-right corner** of the boss icon: shield for PvP, dove for
peace. It is legible to a person at their own monitor, which is why it was reported as an easy read.

**It does not survive capture.** Tested 2026-08-06: screen capture arrives downsampled (2560px
display to ~1389px), so a ~20px icon lands at ~11px and its corner badge at three or four pixels.
Zooming upscales blur; there is no detail to recover. It was possible to see *that* an icon had
something in the corner, and impossible to tell a shield from a dove.

So do not plan on reading mode from the rows. Until a mode has actually been read, **omitting it is
correct** — a name with no mode is incomplete, a name with the wrong mode is misleading.

**Archbosses appear inside boss slots.** Ramux is an archboss and was found among the icons of a
20:00 slot, so the slot's `type` does not fully describe what is spawning; `kind: "archboss"` on the
boss entry is where that distinction lives.

## Step 8 — write, validate, verify, publish

Edit `docs/boss-schedule.json`. **Do not create a second copy anywhere**: the assembly embeds this
exact file as the offline fallback (see the `EmbeddedResource` in
`src/Loadstar.Games.ThroneAndLiberty/Loadstar.Games.ThroneAndLiberty.csproj`), so a duplicate would
drift and leave the schedule current in one place and stale in the other.

Record in the region's `$source` array **what was observed, when, and from which client build** — the
existing entries are the model. That provenance is what lets a future session tell a measurement from
a guess.

Then, in order:

**1. Validate.**

```bash
python .claude/skills/capture-boss-schedule/validate.py
```

It checks JSON validity, recomputes every `localPst` → UTC conversion, rejects boss names absent from
the legend, catches duplicate hourly times and malformed dates, warns on omitted weekdays, and fails
if the file mixes PDT and PST conversions. Then it prints the schedule back **as a Pacific-time
week**. Pass a path to check a candidate file before it overwrites the real one.

**2. Show the player that printed week and get confirmation.** This is the step that matters. The
machine checks prove the file is self-consistent; only the player's own client proves it is *right*.
The whole value of this exercise is that their client is authoritative, which is wasted if a misread
screenshot becomes the new truth unchallenged.

**3. Test.**

```bash
dotnet test tests/Loadstar.Core.Tests/Loadstar.Core.Tests.csproj
```

`BossScheduleTests` covers the stream merge, the guild slots, dated slots, the siege/hourly
collision, and the bundled Americas data. If a test now contradicts the capture, decide which is
right before changing either — a test asserting last month's rotation should be updated; a test
failing because the JSON is malformed should not be.

Note several bundled-data tests pass large `count` values, because the merged schedule yields eight
or nine spawns a day. If you add a stream, counts sized for the old density will silently stop
reaching the thing they assert.

**4. Commit and push.** Pages redeploys in a minute or two. Confirm before telling the player it is
live:

```bash
curl -s https://eugenebednik.github.io/loadstar/boss-schedule.json | head -5
```

The app fetches on startup, validates before adopting, and caches. A player already running the app
picks the change up on next launch; the bundled copy covers them if they are offline.

## Shape reference

```jsonc
"regions": {
  "Americas": {
    "defaultTimeZone": "America/Los_Angeles",   // a default, not a fact about any server
    "hourlySlots": [                            // EVERY day. Written in Pacific order for reading.
      { "time": "18:00", "type": "FieldBosses", "localPst": "11:00" },
      { "time": "01:30", "type": "FieldBosses", "mode": "guild", "localPst": "18:30" }
    ],
    "datedSlots": [                             // explicit dates, UTC
      { "time": "00:30", "type": "TaxDelivery", "mode": "guild", "localPst": "17:30",
        "dates": ["2026-08-31", "2026-09-28"] }
    ],
    "weeklySlots": {                            // per UTC weekday
      "Tuesday": [],                            // empty days are meaningful — write them
      "Wednesday": [
        { "time": "00:00", "type": "ArchBosses", "localPst": "17:00", "bosses": [] },
        { "time": "03:00", "type": "ArchBosses", "localPst": "20:00" }
      ],
      "Monday": [
        { "time": "01:00", "type": "Siege", "mode": "guild", "localPst": "18:00" }
      ]
    }
  }
}
```

`everyDays` defaults to 7 and `since` anchors a longer cycle; **nothing in the file uses them** and
`dates` is the better tool. `bosses` defaults to empty. Keys prefixed `$` are comments.

If a recurrence ever needs a gap longer than **40 days**, raise the day-walk bound in
`BossSchedule.NextSpawns` past it — otherwise the next occurrence falls outside the search window and
the slot silently yields nothing. That bound has already been raised twice for exactly this reason.
