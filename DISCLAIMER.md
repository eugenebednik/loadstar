# Disclaimer

Read this before installing or running Loadstar.

## Loadstar is not affiliated with anyone

Loadstar is an independent project. It is not affiliated with, endorsed by, sponsored by, or
connected to NCSOFT, Amazon Games, questlog.gg, Anthropic, or OpenAI. All trademarks belong
to their respective owners.

## What Loadstar does and does not do

**It does:**

- Capture frames from a window you select, using the public Windows Graphics Capture API —
  the same mechanism OBS, Discord, and Windows Game Bar use.
- Draw an overlay in its own separate, always-on-top window that sits above the game the way
  any other desktop window does.
- Send those captured frames to the AI provider you configured, using your own API key.
- Read publicly available build data from questlog.gg.

**It does not:**

- Inject code into the game process.
- Hook DirectX, the present chain, or any game function.
- Read or write game process memory.
- Send keyboard or mouse input to the game, or automate any action.
- Modify game files, network traffic, or anything the game client ships with.

Every suggestion Loadstar produces is advice on your screen. You perform every action
yourself.

## Anti-cheat and Terms of Service

Throne and Liberty uses Easy Anti-Cheat. Loadstar is designed specifically to stay outside
everything EAC is built to detect — no injection, no hooking, no memory access, no input
synthesis. That design is deliberate and documented in
[docs/anti-cheat-posture.md](docs/anti-cheat-posture.md).

**That said, the following is true and you should weigh it:**

- Amazon Games' Code of Conduct prohibits third-party software that confers an unfair
  advantage. Whether a read-only advisory overlay falls under that is a judgment call that
  belongs to Amazon Games and NCSOFT, not to this project. They have not published a ruling
  on tools of this kind, and they are free to decide against it at any time.
- Anti-cheat systems produce false positives. Overlays that *do* hook the renderer
  (RivaTuner, some SteelSeries and GeForce components) have triggered EAC kicks in Throne
  and Liberty. Loadstar does not hook, but no one can promise you how a future anti-cheat
  update will classify any given process.
- Automated access to questlog.gg's undocumented API is subject to their terms. Loadstar
  caches aggressively and requests rarely to be a good citizen, and supports pasting build
  JSON manually so you can avoid the API entirely.

**You use Loadstar at your own risk.** The authors accept no liability for account
suspension, termination, lost progress, or any other consequence. If you are not comfortable
with that, do not use it.

## Your data

Loadstar has no servers and no telemetry. Nothing is collected, transmitted to, or stored by
this project.

Screenshots you capture are sent to the AI provider you configured, under your own API key
and their terms and privacy policy. Your API key is encrypted at rest using Windows DPAPI
and never leaves your machine except as an auth header to that provider.

Captured frames may include anything visible in the captured window — chat messages,
character names, guild names, and other players' names. Restrict capture to the regions you
actually need, and be aware you may be sending other people's names to a third party.

## No warranty

Loadstar is provided "as is", without warranty of any kind. See [LICENSE](LICENSE).
