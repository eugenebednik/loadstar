# Loadstar

An AI game companion overlay for Windows. Loadstar watches your screen, compares what it
sees against the build you're aiming for, and tells you the highest-value thing to do next
with the resources you actually have.

First supported game: **Throne and Liberty**.

> [!IMPORTANT]
> Loadstar never touches the game. It does not inject code, hook the renderer, read process
> memory, or send input. It captures the screen the same way OBS does and draws its own
> separate window on top. See [docs/anti-cheat-posture.md](docs/anti-cheat-posture.md) for
> the full design constraints and [DISCLAIMER.md](DISCLAIMER.md) before you use it.

## What it does

- **Reads your state from the screen.** On a cadence you set, and only after you explicitly
  consent, it snapshots the game window and extracts your gear, levels, currencies, tokens,
  and inventory.
- **Knows where you're going.** Paste a [questlog.gg](https://questlog.gg) build URL in
  settings. Loadstar pulls the full target loadout — weapons, gear, runes, traits, skills.
- **Tells you what to do next.** The gap between current and target is handed to Claude or
  GPT, which returns a ranked, resource-aware plan: what to upgrade, what to hold, what not
  to waste your gold and tokens on.
- **Remembers the whole session.** Each play session is one ongoing conversation, not a series
  of unrelated questions. The assistant knows what it already told you, whether you did it, and
  which way your gold has been moving — so it can say "you're 40k short of the upgrade you've
  been saving for" instead of re-deriving your situation from scratch every two minutes. See
  [docs/conversation-model.md](docs/conversation-model.md) for how that stays affordable.
- **Tracks world bosses.** A per-region, per-server-timezone spawn schedule with countdowns
  and configurable pre-spawn alerts.

## Status

Early. See the [project board](../../projects) for what's landed and what's next.

## Requirements

- Windows 10 version 2004 (build 19041) or newer — required for the capture API. Windows
  Graphics Capture itself dates to 1903, but capturing without a UI thread to pump needs
  `Direct3D11CaptureFramePool.CreateFreeThreaded`, which arrived in 2004.
- The game running in **borderless windowed** mode (see [Fullscreen](#fullscreen) below)
- An API key for **Anthropic**, **OpenAI**, or **Google Gemini** — pick the provider in Settings.
  Gemini has a free tier, so it is the one that works without a billing account.

No .NET install needed — the installer ships a self-contained build.

## Install

**[Download the latest installer](../../releases/download/latest-build/Loadstar-x64.msi)** and run it.

That link always points at the most recent build of `main`. Installers are also published per
language — English, Russian, Ukrainian, Spanish, German, French, Japanese, Korean and Traditional
Chinese — on the [latest-build release](../../releases/tag/latest-build); pick
`Loadstar-<version>-x64-<lang>.msi`. Pinned versions live under [Releases](../../releases).

Everything needed to run is inside the MSI. There is no separate .NET download, and no prerequisite
beyond the Windows version above.

To build from source:

```bash
dotnet publish src/Loadstar.App -c Release -r win-x64 --self-contained
```

To build the installer locally, which needs the WiX CLI:

```bash
dotnet tool install --global wix --version 5.0.2
wix extension add --global WixToolset.UI.wixext/5.0.2
wix build installer/Loadstar.wxs -arch x64 -culture en-US -define Version=0.1.0 \
  -bindpath "publish=$PWD/publish" -bindpath "installer=$PWD/installer" \
  -ext WixToolset.UI.wixext -out artifacts/Loadstar-x64.msi
```

## Before it can help you: expand the currency bar

> [!WARNING]
> Throne and Liberty collapses the currency bar by default, showing **only gold**. Click the
> arrow at the top-left ("View all currency") so the full row of tokens is visible along the
> top edge, and leave it expanded while Loadstar runs.

This is not a nicety. The entire point of the tool is telling you how to spend resources — and
with the bar collapsed the assistant can see exactly one of them. It will still produce
confident advice, and that advice will be wrong, because it is reasoning about a wallet it
cannot see.

Loadstar checks for this after every capture, not just at startup, and says so loudly if the
bar gets collapsed mid-session.

There is a second, one-time step worth doing: the bar shows **icons and numbers with no
names**, so open the full currency window once and hit the capture hotkey. Loadstar keeps that
single image as a reference and pairs it with every later reading, which is what lets icons
resolve to the right currency names.

## Configuration

Everything lives in Settings, and everything is editable:

| Setting | Notes |
| --- | --- |
| Game | Currently Throne and Liberty |
| Target build | A questlog.gg build URL, or pasted build JSON |
| AI provider | Anthropic or OpenAI |
| Model | Claude Sonnet/Opus, or GPT 5.5+ |
| API key | Encrypted at rest with Windows DPAPI, scoped to your user account |
| Capture cadence | How often to snapshot, and which regions to look at |
| Region / server | Drives the world boss schedule |
| Overlay | Position, opacity, click-through, hotkeys |

Your API key is never sent anywhere except the provider you chose. Screenshots go to that
provider and nowhere else. Loadstar has no backend.

## Fullscreen

A non-injecting overlay cannot draw over a game in **exclusive fullscreen** — that's a
Windows compositor rule, not a Loadstar limitation, and working around it would mean
hooking the game, which is exactly what this project refuses to do. Run Throne and Liberty
in borderless windowed mode. Capture and suggestions still work in exclusive fullscreen;
only the on-screen overlay is unavailable, so it falls back to a toast in the corner of
your second monitor if you have one.

## Cost

You pay your provider directly, per snapshot analyzed. A snapshot is one image plus a small
text payload. Loadstar shows a running estimate in the status bar and lets you set a hard
monthly ceiling — it stops analyzing when you hit it rather than surprising you.

## License

[MIT](LICENSE). Not affiliated with, endorsed by, or connected to NCSOFT, Amazon Games, or
questlog.gg.
