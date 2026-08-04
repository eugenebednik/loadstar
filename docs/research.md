# Research notes

Findings that shaped the design. Dated 2026-08-03; re-verify anything load-bearing before
relying on it.

## questlog.gg has a usable public API

questlog.gg is a Next.js app backed by tRPC. The endpoints are unauthenticated and return
clean JSON. Base: `https://questlog.gg/throne-and-liberty/api/trpc/`.

### Fetching a build

```
GET characterBuilder.getCharacter?input={"slug":"<build-slug>","url":"<build-slug>"}
```

Both fields take the **same value**: the last path segment of a build URL. For
`https://questlog.gg/throne-and-liberty/en/character-builder/Y34OLziq81yK/MournfulGripOfFerocity`
the slug is `MournfulGripOfFerocity`. The other segment is the author's profile slug and is
not needed. Passing the profile slug in `slug` returns `{"status":"NOT_FOUND"}`.

That naming is a trap worth remembering — `slug` is not the user slug.

### Build payload shape

Equipment slots (`equipmentMainHand`, `equipmentOffHand`, and the armor/accessory slots)
each carry everything needed to describe a target loadout:

```jsonc
{
  "id": "sword2h_aa_t2_raid_001",           // item id, encodes weapon/tier/source
  "perk": "perk_sword2h_aa_t3_boss_001",
  "runes": {
    "0": { "lvl": 120, "runeId": "Weapon_Atk_Rune_Usable_kAA2_001", "statId": "melee_accuracy" }
  },
  "heroic":  { "1": "all_accuracy", "2": "melee_accuracy" },
  "traits":  { "all_accuracy": 800, "all_double_attack": 800 },
  "potential": null,
  "resonance": null,
  "uniqueTraits": { "per": 8 }
}
```

This is the whole reason the project is viable: the *target* state is exact structured data,
so the AI is only ever asked to read the *current* state off the screen. That is a far
easier and more reliable job than asking it to reason about both sides from pixels.

### Other useful procedures

| Procedure | Returns |
| --- | --- |
| `characterBuilder.searchCharacters?input={"searchTerm":"","tags":[],"page":1}` | Paginated builds **with full equipment inline** |
| `characterBuilder.getPreviewEquipmentItems` | Item catalogue for resolving ids to names/icons |
| `characterBuilder.getAttributeStats` | Stat definitions |
| `eventCalendar.getFieldBossEntries?input={"language":"en"}` | Field boss roster (id, name, icon, grade) |

`getPreviewEquipmentItems` matters: item ids like `sword2h_aa_t2_raid_001` are opaque, and
the AI will be comparing against what the player sees on screen, which is display names. We
cache this catalogue and resolve ids locally rather than making the model guess.

### Caveats

- Undocumented and unversioned. It can change without notice, so every call is behind an
  interface with a manual-JSON-paste fallback.
- Cloudflare sits in front of the site. Requests need a real `User-Agent` and should be
  infrequent. We cache builds indefinitely and refresh only on explicit user action.
- questlog's own ToS page did not render for automated fetching, so the automated-access
  terms are unconfirmed. Treated conservatively: cache hard, request rarely, and give users
  a path that never calls the API.

## World boss timers are a fixed schedule, not a live feed

This was the useful surprise. The obvious assumption — scrape a timer site — is wrong.

questlog's event calendar renders a **static weekly grid**: a region selector (Americas /
Europe / Asia), a UTC offset, and fixed daily slots (15:00, 18:00, 19:00, 21:00, 22:00…)
with the boss rotation as client-side data. thronewatch.app does the same thing and stores
everything in browser local storage, computing rotation days from the 03:00 server reset.

Reporting on European servers is that Amazon never exposed a live spawn API at all.

So the schedule is **deterministic**: region + server timezone + day of week + reset
boundary is enough to compute every spawn locally. We ship the rotation as editable JSON and
do no runtime scraping. That's more reliable than any scraper, has no failure mode when a
site changes its markup, and sidesteps the ToS question entirely.

The one thing that needs maintenance is the rotation table itself, when a patch changes it.
That's a data file update, not a code change.

## Anti-cheat

Throne and Liberty runs Easy Anti-Cheat. Community reports show EAC kicks associated with
overlays that hook the present chain — RivaTuner, SteelSeries GG, some GeForce Experience
versions — and with the Steam overlay.

The common factor in every one of those is **injection or renderer hooking**. That is the
single design constraint that shapes this entire project: see
[anti-cheat-posture.md](anti-cheat-posture.md).

Amazon's Code of Conduct and Anti-Cheat Software Disclosure pages are served behind bot
protection and could not be fetched for exact wording. The prohibition is understood to
target unfair advantage and automation. Since that leaves a genuine judgment call about
advisory tools, the disclaimer states it plainly rather than claiming a clearance nobody
has given.

## Sources

- [questlog.gg character builder](https://questlog.gg/throne-and-liberty/en/character-builder)
- [questlog.gg event calendar](https://questlog.gg/throne-and-liberty/en/event-calendar)
- [Throne Watch](https://thronewatch.app/)
- [GamesLantern boss timer](https://throneandliberty.gameslantern.com/boss-timer)
- [Mein-MMO on EU timer API availability](https://mein-mmo.de/en/throne-and-liberty-boss-timer-never-miss-a-boss,1196918/)
- [Throne and Liberty legal index](https://www.playthroneandliberty.com/en-us/legal)
- [EAC kick troubleshooting / overlay conflicts](https://www.gameleap.com/articles/throne-and-liberty-kicked-by-easy-anti-cheat-fix)
