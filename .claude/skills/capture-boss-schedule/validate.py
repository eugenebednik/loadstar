#!/usr/bin/env python3
"""Check docs/boss-schedule.json and print it back as a Pacific-time week.

Two jobs, and the second one matters more.

CHECKING catches the error class a human reviewer reliably misses: slot times are stored in UTC
while the client shows Pacific, so every row was converted by hand and a one-hour or one-day slip
still reads as a perfectly plausible evening. Every such slot carries `localPst`, so the conversion
is checkable arithmetic rather than a matter of trust.

PRINTING is what the capture is actually confirmed against. The person running this has the game
open; a Pacific-time week they can read top-to-bottom against their own client is the only real
verification this file gets. Machine checks confirm the file is self-consistent, not that it
matches the game.

    python .claude/skills/capture-boss-schedule/validate.py [path-to-schedule.json]

The path is optional and defaults to docs/boss-schedule.json. Pass one to check a candidate before
it overwrites the real file.

Exit code 1 on any error. Warnings do not fail — they are things to look at, not things that are
wrong. Standard library only, and no timezone database: Pacific is applied as a fixed offset,
because the whole DST question this file records is still open and pretending otherwise would hide
it.
"""

import json
import sys
from datetime import date, datetime, timedelta
from pathlib import Path

SCHEDULE = Path(__file__).resolve().parents[3] / "docs" / "boss-schedule.json"
LEGEND = Path(__file__).resolve().parent / "icon-legend.json"

# The two Pacific offsets. The capture was made in PDT; November decides whether stored times
# follow the clock or stay put. See $timeBasis in the file.
PDT = timedelta(hours=7)
PST = timedelta(hours=8)

DAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]

errors: list[str] = []
warnings: list[str] = []


def parse_time(value: str) -> timedelta:
    parts = [int(p) for p in value.split(":")]
    return timedelta(hours=parts[0], minutes=parts[1] if len(parts) > 1 else 0)


def fmt(delta: timedelta) -> str:
    total = int(delta.total_seconds()) % 86400
    return f"{total // 3600:02d}:{total % 3600 // 60:02d}"


def load_legend() -> set[str] | None:
    """Every name the client is known to use, or None if the legend is unreadable."""
    try:
        entries = json.loads(LEGEND.read_text(encoding="utf-8"))["bosses"]
    except (FileNotFoundError, KeyError, json.JSONDecodeError) as ex:
        warnings.append(f"could not read {LEGEND.name} ({ex}), so boss names are unchecked")
        return None

    return {entry["name"] for entry in entries}


def check_bosses(slot: dict, where: str, legend: set[str] | None) -> None:
    """A name not in the legend is a misread or a new boss. Either way it is not ready to ship.

    This is the closed-vocabulary guard. The schedule's hardest rule is never to invent a name,
    because a plausible wrong one is indistinguishable from a right one until the player travels
    somewhere empty — and by then they have wasted the trip and stopped trusting the timer.
    """
    for entry in slot.get("bosses", []):
        name = entry if isinstance(entry, str) else entry.get("name")

        if not name or not str(name).strip():
            errors.append(f"{where}: a bosses entry has no name")
            continue

        if legend is not None and name not in legend:
            errors.append(
                f"{where}: {name!r} is not in {LEGEND.name}. Either it is a misread, or it is new -- "
                f"confirm it against the client's Content Settings window and add it to the legend "
                f"first. Do not ship a name that has not been read off the client."
            )


def check_local(slot: dict, where: str) -> None:
    """Verify `time` really is `localPst` converted to UTC."""
    if "localPst" not in slot:
        warnings.append(f"{where}: no localPst, so the UTC time cannot be cross-checked")
        return

    utc = parse_time(slot["time"])
    local = parse_time(slot["localPst"])

    # Either offset is accepted; which one is reported, because a file mixing them is a real bug
    # and is invisible otherwise.
    for name, offset in (("PDT", PDT), ("PST", PST)):
        if fmt(local + offset) == fmt(utc):
            slot["_offset"] = name
            return

    errors.append(
        f"{where}: time {slot['time']}Z does not match localPst {slot['localPst']} "
        f"under either offset (expected {fmt(local + PDT)}Z in PDT or {fmt(local + PST)}Z in PST)"
    )


def main() -> int:
    target = Path(sys.argv[1]) if len(sys.argv) > 1 else SCHEDULE

    try:
        data = json.loads(target.read_text(encoding="utf-8"))
    except FileNotFoundError:
        print(f"not found: {target}")
        return 1
    except json.JSONDecodeError as ex:
        print(f"{target.name} is not valid JSON: {ex}")
        return 1

    basis = data.get("timeBasis")

    if basis != "utc":
        warnings.append(
            f'timeBasis is {basis!r}, not "utc" -- these checks assume UTC storage and are '
            f"meaningless otherwise"
        )

    offsets = set()
    legend = load_legend()

    for region_name, region in data.get("regions", {}).items():
        weekly = region.get("weeklySlots")
        hourly = region.get("hourlySlots", [])
        dated = region.get("datedSlots", [])

        if weekly is None and not hourly and not dated:
            # Legacy dailySlots only, or genuinely no data. Both are expected for some regions.
            continue

        if weekly is not None:
            missing = [d for d in DAYS if d not in weekly]

            if missing:
                # Absent and empty parse the same, but they SAY different things: empty is
                # "confirmed nothing here", absent is "nobody looked".
                warnings.append(
                    f"{region_name}.weeklySlots omits {', '.join(missing)} -- write them as [] if "
                    f"the client really shows nothing, so the file records that it was checked"
                )

            for day, slots in weekly.items():
                for i, slot in enumerate(slots):
                    where = f"{region_name}.weeklySlots.{day}[{i}]"
                    check_local(slot, where)
                    check_bosses(slot, where, legend)
                    offsets.add(slot.get("_offset"))

        seen: dict[str, int] = {}

        for i, slot in enumerate(hourly):
            where = f"{region_name}.hourlySlots[{i}]"
            check_local(slot, where)
            check_bosses(slot, where, legend)
            offsets.add(slot.get("_offset"))

            if slot["time"] in seen:
                errors.append(f"{where}: duplicate time {slot['time']} (also index {seen[slot['time']]})")

            seen[slot["time"]] = i

        for i, slot in enumerate(dated):
            where = f"{region_name}.datedSlots[{i}]"
            check_local(slot, where)
            check_bosses(slot, where, legend)
            offsets.add(slot.get("_offset"))

            dates = slot.get("dates", [])

            if not dates:
                errors.append(f"{where}: datedSlots entry with no dates never fires -- remove it or add dates")

            for value in dates:
                try:
                    date.fromisoformat(value)
                except (TypeError, ValueError):
                    errors.append(f"{where}: {value!r} is not an ISO date (YYYY-MM-DD)")

    offsets.discard(None)

    if len(offsets) > 1:
        errors.append(
            f"the file mixes Pacific offsets ({', '.join(sorted(offsets))}) -- every slot must be "
            f"converted in the same season or half of them are an hour out"
        )

    print(f"{target.name}: parsed, timeBasis={basis!r}, "
          f"offset={'/'.join(sorted(offsets)) if offsets else 'unverifiable'}\n")

    # DIAGNOSIS BEFORE PRESENTATION, and this ordering is load-bearing. Rendering first cost the
    # whole report once: a malformed date crashed the renderer and the operator got a traceback
    # instead of the line saying which date was malformed. A tool for finding mistakes has to survive
    # them.
    for warning in warnings:
        print(f"  warn   {warning}")

    for error in errors:
        print(f"  ERROR  {error}")

    if warnings or errors:
        print()

    if errors:
        print(f"{len(errors)} error(s). Fix these before committing.\n")

    try:
        render(data)
    except Exception as ex:  # noqa: BLE001 — presentation must not be able to hide the diagnosis
        print(f"  (could not render the week: {type(ex).__name__}: {ex})\n")

    if errors:
        return 1

    print("No errors. Now the part that actually matters: check the week above against the client.")
    return 0


def render(data: dict) -> None:
    """Print a Pacific-time week per region, for eyeball comparison against the live client."""
    offset = PDT

    for region_name, region in data.get("regions", {}).items():
        weekly = region.get("weeklySlots")
        hourly = region.get("hourlySlots", [])
        dated = region.get("datedSlots", [])

        if weekly is None and not hourly and not dated:
            continue

        print(f"=== {region_name}, PACIFIC time (as the client shows it) ===\n")

        # A representative week. Rendered from the UTC data the app reads, converted back, so this
        # exercises the same roll the parser does rather than re-reading localPst.
        #
        # NINE UTC days for seven Pacific ones. The week's edges land mid-Pacific-day, so the first
        # and last Pacific dates are always partial and get dropped — at seven days that silently ate
        # Sunday, and Sunday is where the siege lives.
        start = date(2026, 8, 10)  # a Monday
        rows: dict[date, list[tuple[timedelta, str]]] = {}

        for day_index in range(9):
            utc_day = start + timedelta(days=day_index)
            slots = list(weekly.get(DAYS[utc_day.weekday()], [])) if weekly else []
            slots += hourly
            slots += [s for s in dated if str(utc_day) in s.get("dates", [])]

            for slot in slots:
                instant = datetime.combine(utc_day, datetime.min.time()) + parse_time(slot["time"])
                local = instant - offset
                label = slot["type"]

                if slot.get("mode"):
                    label += f"  [{slot['mode']}]"

                if slot.get("bosses"):
                    names = [b if isinstance(b, str) else b.get("name", "?") for b in slot["bosses"]]
                    label += "  " + ", ".join(names)

                rows.setdefault(local.date(), []).append((timedelta(hours=local.hour, minutes=local.minute), label))

        # Partial edges dropped, then exactly one week kept.
        for day in sorted(rows)[1:-1][:7]:
            print(f"  {day:%a %d/%m}")

            for time_of_day, label in sorted(rows[day]):
                print(f"    {fmt(time_of_day)}  {label}")

            print()

        if not dated:
            continue

        # Dated events sit outside any representative week, so they are listed rather than rendered.
        # They are the ones most worth reading carefully: they were captured for specific days and
        # they go silent when the list runs out.
        print("  dated events (Pacific dates, converted back from the stored UTC ones)\n")

        for slot in dated:
            local_time = fmt(parse_time(slot["time"]) - offset)

            for value in sorted(slot.get("dates", [])):
                try:
                    utc_date = date.fromisoformat(value)
                except (TypeError, ValueError):
                    # Already reported as an error above; skip it here rather than crashing the
                    # renderer and burying every other finding.
                    print(f"    (unparseable date {value!r})")
                    continue

                local = datetime.combine(utc_date, datetime.min.time()) + parse_time(slot["time"]) - offset
                mode = f"  [{slot['mode']}]" if slot.get("mode") else ""
                print(f"    {local:%a %d/%m} {local_time}  {slot['type']}{mode}   (stored {value} {slot['time']}Z)")

        print()


if __name__ == "__main__":
    sys.exit(main())
