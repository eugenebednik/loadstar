# Boss timer

A self-contained feature: no capture, no AI, no overlay. It shares nothing with the advice
engine, which is why it can be built in parallel.

## There is no official API — and the game is the best source

Confirmed 2026-08-04. **Amazon has not activated a spawn API**, and third-party timer developers
are openly waiting on it. GamesLantern's timer covers **Korean servers only** (NCSoft's version),
not Amazon's global release. This is why questlog's regional coverage is uneven and why no
external timer reliably covers EU — the underlying data source does not exist.

So local computation is not a workaround around a missing integration. It is the only approach
available to anyone, and every competing tool is in the same position.

**The strongest source is the player's own client.** Opening the large in-game map lists the
day's bosses and their spawn times down the left side of the screen. That is:

- **per-server accurate by definition** — it is that player's server, not a regional guess
- **available for every region**, including the ones no external timer covers
- **already within Loadstar's capabilities** — it is a screen capture, the thing this app does

That reframes the feature. Rather than shipping a schedule table and hoping it matches the
player's server, **prompt the player to open the map once and capture it**, then compute
everything from their own data. The bundled JSON becomes a fallback and a sanity check, not the
source of truth.

It also fits the existing pattern exactly: the same user-initiated capture already used for the
named-currency reference and the Combat Power tooltip. Same hotkey, same consent model, same
constraint that Loadstar cannot open the map itself because that would be input.

Worth confirming the map screen is text-labelled before committing to this — the currency bar
and inventory were not, and that assumption has burned this project once already.

## Why the bundled table is local data, not a scraper

The obvious design — poll a timer site — is wrong. questlog's event calendar renders a **fixed
weekly grid**: a region selector, a UTC offset, and the same daily slots repeating. thronewatch
does the same and keeps everything in browser local storage, computing rotation days from the
03:00 reset. Reporting is that Amazon never exposed a live spawn API at all.

So spawn times are **deterministic**: region + server timezone + weekday + reset boundary is
enough to compute every spawn offline. That is more reliable than any scraper, has no failure
mode when a site changes its markup, no rate limit, works offline, and sidesteps the ToS
question entirely.

The cost is that [`boss-schedule.json`](../src/Loadstar.Games.ThroneAndLiberty/Data/boss-schedule.json)
needs a refresh when a patch changes the rotation. That is a data edit, not a code change —
which is why it ships as JSON rather than as constants.

## What's confirmed and what isn't

Captured 2026-08-04 from the live calendar, patch 4.5.0:

**Confirmed — Americas daily slots:** 12:00, 15:00, 18:00 Dynamic Events · **19:00 Field
Bosses** · 21:00 Dynamic Events · **22:00 Field Bosses**. Reset at 03:00 server time.

**Confirmed — field boss roster** (7): Adentus, Talus, Grand Aelon, Chernobog, Cornelius,
Junobote, Daigon.

**Not captured:**

- **Europe and Asia slot tables.** Switch the region selector on questlog's calendar and record
  them. Do not assume they mirror Americas.
- **Which boss occupies which slot on which weekday.** Open each day on the calendar. The timer
  is useful without this — "Field Bosses in 18 minutes" is already actionable — so treat it as a
  refinement, not a blocker.
- **Boss species.** Not in the `getFieldBossEntries` payload, and worth chasing: species pairs
  with the character sheet's Species tab, which turns a generic alert into *"Daigon is a Demon —
  swap to Demon damage before 21:00."* That is the feature's most interesting output.
- **Arch boss schedule.** Separate, less frequent. Ramux landed in 4.5.0 (Stillreach, two-phase
  with Atirat).

## Design notes worth having up front

**Timezone is the whole problem.** Store the schedule in server-local wall-clock time and convert
for display; never store computed UTC instants, because DST shifts them. Use `TimeZoneInfo` with
IANA ids (.NET 8 accepts them on Windows). The user picks their server's timezone — the region
default in the JSON is a starting suggestion, not a fact about their server.

**Events perturb the base table.** The Boosted Arch Bosses event (2026-07-28 → 2026-08-11) had
four arch bosses spawning simultaneously. The schedule model needs a temporary-override concept
rather than assuming the base rotation always holds, or it will confidently announce wrong times
during every event.

**Notifications.** Windows toast via `Microsoft.Toolkit.Uwp.Notifications` / `AppNotification`.
Requires a registered AUMID to show reliably from a desktop app — worth verifying early, since
it affects the installer. Alert offsets are configurable and default to 15 and 5 minutes; an
alert that fires as the boss spawns is useless, since travel time is the point.

**Don't notify into a void.** If the game isn't running, the user is not about to fight a boss.
Suppressing alerts when no TL process exists is a small touch that prevents the feature becoming
something the user mutes — and process presence is a read-only check, so it stays inside the
anti-cheat contract.

## Testing

Deterministic input and output, so this is genuinely unit-testable with no game running: feed a
fixed "now" plus a region and timezone, assert the next N spawns. Include a DST boundary case
and a reset-boundary case (23:00 on the day before a rotation change), because those are where
schedule code actually breaks.
