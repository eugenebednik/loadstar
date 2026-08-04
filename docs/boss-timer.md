# Boss timer

A self-contained feature: no capture, no AI, no overlay. It shares nothing with the advice
engine, which is why it can be built in parallel.

## Why it's local data, not a scraper

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
