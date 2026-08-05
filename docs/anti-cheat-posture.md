# Anti-cheat posture

This document is a contract. Every line under "Forbidden" is a thing this codebase will not
do, and a reviewer should reject any PR that introduces one.

## The rule

**Loadstar is an observer. It reads pixels and draws its own window. It never touches the
game.**

Throne and Liberty runs Easy Anti-Cheat. EAC is built to detect injection, hooking, memory
tampering, and automation. Loadstar does none of those, and the point of writing it down is
that "just this once, to fix the fullscreen case" is exactly how a project like this ends up
getting its users banned.

## Forbidden

Never, in any code path, behind any flag, for any reason:

| Forbidden | Why it matters |
| --- | --- |
| DLL injection into the game process | The single most-detected cheat behaviour |
| Hooking D3D/DXGI, `Present`, or any game function | What RivaTuner and SteelSeries GG do, and what gets people kicked from TL |
| `ReadProcessMemory` / `WriteProcessMemory` on the game | Memory access is indistinguishable from a cheat at the anti-cheat layer |
| `SendInput`, `keybd_event`, `PostMessage` to the game window | Automation. Turns an advisor into a bot |
| Reading or modifying game files, configs, or packets | Client tampering |
| Driver-level or kernel-mode anything | Nothing here needs it |
| Parsing network traffic | Even read-only, this is protocol reverse-engineering |

If a feature seems to require one of these, the feature does not ship.

## Allowed

- **Windows Graphics Capture** (`Windows.Graphics.Capture`) for per-window frame capture.
  Public API, no hooking, requires no elevation. It is what OBS, Discord, and Windows Game
  Bar use to capture games.
- **A separate top-level WPF window**, `Topmost`, layered, with `WS_EX_TRANSPARENT` and
  `WS_EX_NOACTIVATE` for click-through. This is an ordinary desktop window. The compositor
  puts it above the game the same way it would put Notepad above the game.
- **Global hotkeys** via `RegisterHotKey`, which routes through the OS, not the game.
- **Outbound HTTPS** to the configured AI provider and to questlog.gg.

## The fullscreen consequence

Exclusive fullscreen hands the display to the game and no other window composites over it.
There is a well-known way around that. It is called hooking the present chain, and it is on
the forbidden list.

So Loadstar does not solve it. In exclusive fullscreen the overlay simply does not draw;
capture and analysis still work, and suggestions fall back to a second-monitor toast. The
documented fix for users is borderless windowed mode.

Accepting a degraded feature here is the correct trade. Users can be told to change a video
setting. They cannot be un-banned.

## Consent

Screen capture is off until the user turns it on, and the first-run flow says plainly what
gets captured and where it is sent. A capture indicator is visible in the overlay whenever
capture is active — the user should never be unsure whether their screen is being read.

## Enforcement

`tests/Loadstar.Core.Tests/AntiCheatPostureTests.cs` scans the compiled assemblies for
P/Invoke declarations of the forbidden APIs and fails the build if one appears. It is a
blunt instrument and it is meant to be — it turns this document from a good intention into
something CI checks.

It reads IL metadata rather than loading assemblies, so it catches a *declaration* even in
code nothing ever calls — which is exactly where something like this would get parked. Three
checks run, deliberately overlapping:

| Check | Catches |
| --- | --- |
| Forbidden entry points | The functions in the table above, by name, in any module |
| Module allowlist | Native libraries nobody thought to forbid — `detours.dll`, a wrapped `d3d9.dll` |
| Recorded baseline | *Any* change to the native surface, so additions surface in review |

The allowlist deliberately does not pre-authorise `user32` or `kernel32`. Hotkeys are
permitted by this document, so whoever adds `RegisterHotKey` will have to add `user32` — and
that is precisely the moment the surrounding P/Invokes deserve a second reading.

The whole native surface is five functions, all in
`src/Loadstar.Capture.Windows/NativeMethods.cs`: three `combase` calls to reach the
GraphicsCaptureItem interop factory, and two `d3d11` calls that create **Loadstar's own**
device to receive frames into. Window discovery uses `System.Diagnostics.Process` and needs
no P/Invoke and no handle to the game process.

The test has been verified to fail: adding `ReadProcessMemory` and `SendInput` to the capture
assembly trips all three checks. A guard that has never failed is not known to work.
