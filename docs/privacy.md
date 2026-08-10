# Privacy Policy

**Effective 5 August 2026.** Covers the Loadstar desktop application for Windows.

Loadstar has **no backend**. There is no Loadstar server, no account, no analytics and no telemetry.
We collect nothing about you, because there is nowhere for it to go and nothing built to receive it.

What follows is a specific account of what the app reads, what leaves your machine, and where it
goes. All of it is checkable against [the source](https://github.com/eugenebednik/loadstar) — that is
the point of publishing it.

## What Loadstar reads

**One window, which you choose.** Loadstar captures a single window through the Windows Graphics
Capture API, the same mechanism OBS uses. It captures a *window handle*, not your screen and not your
desktop, so other applications, notifications and other monitors are not in the image.

Which window is your explicit configuration, never a guess. You point Loadstar at the game process or
executable once, and it captures that. This is deliberate: an earlier version matched windows by
title, and a search for the game's name matched a **browser tab** with a build guide open — one step
from sending a browser window to a third party. Targeting has been explicit ever since.

**Only after you agree.** The first capture is gated behind a consent dialog stating what is
captured and where it is sent. Nothing is captured before you accept.

**And you see every image before it is sent.** Each screenshot appears in the ask window at full
size, above the words "this exact image is what gets sent", with a delete button on it. Nothing
leaves until you press Ask, and anything you delete first is never transmitted. This is the control
that matters, because it is the one you can verify yourself.

**On blacked-out regions.** Loadstar can paint masks onto the raw pixels before encoding, so masked
areas would never exist in any file or transmission, and it reports how many it applied. For Throne
and Liberty it currently applies **none**, and that is a deliberate reversal: a fixed mask over the
bottom-left corner was removed in v0.20.0 because it was blacking out the character sheet's stat
column — the exact numbers the advice is built from. The chat window it was meant to cover is
draggable, resizable, and drawn as plain text over the game world with no panel behind it, so no
fixed rectangle covers it reliably and there is no shape to detect. If you would rather your chat
were not in frame, close or move it before capturing, or delete that screenshot in the ask window.

**Never your game files.** Loadstar does not read, modify or parse game files, configuration, process
memory or network traffic, and does not inject code or send input. That is a hard project constraint
enforced by an automated test that fails the build — see
[anti-cheat-posture.md](anti-cheat-posture.md).

## What leaves your machine

Two destinations. There are no others.

### 1. The AI provider you chose

To produce advice, Loadstar sends the captured image and a text payload to **the single provider you
selected in Settings**, authenticated with **your own API key**:

| Provider | Endpoint |
| --- | --- |
| Anthropic | `api.anthropic.com` |
| OpenAI | `api.openai.com` |
| Google | `generativelanguage.googleapis.com` |

Sent: the screenshot as shown to you in the ask window, text read from it, Loadstar's own
instructions and game knowledge, and
the earlier turns of the current session so the assistant does not repeat itself.

**Your relationship for that data is with the provider, not with us.** How they handle it — retention,
human review, whether they train on it — is governed by their privacy policy and the terms of the
account your API key belongs to. Read the terms of whichever you pick; they differ.

### 2. questlog.gg

Loadstar fetches your target build and its reference catalogues from questlog.gg's public API. That
request carries the build identifier you pasted and a User-Agent identifying Loadstar. No account, no
key, and nothing about your character beyond which public build you asked for. questlog.gg is an
unaffiliated third party with its own privacy policy.

## What stays on your machine

Everything Loadstar stores is in `%LOCALAPPDATA%\Loadstar\`, and none of it is transmitted:

| File | Contents |
| --- | --- |
| `settings.json` | Preferences — build URL, chosen window, hotkeys, region, provider and model |
| `credentials-<provider>.bin` | Your API key, encrypted with Windows DPAPI at `CurrentUser` scope, so another account on the same machine cannot read it |
| `crash.log` | Exception stack traces, appended when the app fails, so bugs can be reported with detail |

**Your API key goes to exactly one place: the provider it belongs to.** It is never sent to us.

**Captures and advice are never saved.** The session conversation, including every image, is held in
memory and discarded when Loadstar closes. The application writes no image files. (The repository
also contains a developer capture-test CLI that does save PNGs; it is not part of the installer.)

`crash.log` never leaves your machine, but stack traces can contain file paths. Read it before
attaching it to a bug report.

## Children

Loadstar is not directed at children, and collects nothing from anyone of any age.

## Your control

- **Stop anything leaving.** Remove your API key in Settings, or quit the app. With no key, nothing
  is sent to a provider.
- **Delete everything Loadstar holds.** Delete `%LOCALAPPDATA%\Loadstar\`. That folder is the entire
  footprint — uninstalling removes the program, deleting that folder removes your data.
- **Reduce what is visible.** Close or move any panel you would rather not send before you capture,
  and delete individual screenshots in the ask window before pressing Ask.
- **Ask a provider to delete their copy.** Data already sent is held under their policy, so that
  request goes to them, not to us.

## Changes

Changes are recorded in this file's public git history, so you can read not only the current policy
but every earlier version and the exact date it changed. Substantive changes also bump the consent
dialog's version, so you are asked again rather than silently opted in.

## Contact

Open an issue at <https://github.com/eugenebednik/loadstar/issues>.

---

Loadstar is not affiliated with, endorsed by, or connected to NCSOFT, Amazon Games, or questlog.gg.
