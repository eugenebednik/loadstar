# Conversation model

Loadstar does not send one-shot requests. Each play session is a **single ongoing
conversation** with the assistant, so it remembers everything that has happened: what you had
an hour ago, what it already told you to do, and whether you did it.

That is a better product and it is also the harder thing to build correctly. This is how.

## Why stateless would have been wrong

A stateless snapshot analyser can only ever say "given this screen, do X". It re-derives your
situation from scratch every time, so it cannot notice that you ignored its last three
suggestions, cannot see that your gold went *down*, and will happily tell you to buy the same
item twice. The interesting advice — "you're 40k short of the upgrade you've been saving for,
and you just spent 30k on something off-plan" — only exists if the assistant has history.

## The shape

One `AdviceSession` per play session. It owns a message list that grows:

```
system:     role, target build, game rules, output contract   ← cached, never changes
user:       [snapshot image] + observation metadata
assistant:  advice JSON
user:       [snapshot image] + observation metadata
assistant:  advice JSON
...
```

The target build is pinned into the system prompt rather than resent per turn, because it is
stable for the whole session and that makes it part of the cacheable prefix.

## The two costs this creates, and what we do about them

Replaying history every turn is what makes conversation work, and it is also what makes it
expensive. Both problems have real fixes.

### Cost: re-sending the prefix every turn

Solved by **prompt caching**. The system prompt plus the target build is a large, byte-stable
prefix, so it goes behind a `cache_control` breakpoint and bills at roughly a tenth of input
price on every turn after the first.

This is why the target build lives in the system prompt and why nothing volatile — no
timestamp, no session id, no turn counter — is allowed anywhere near it. A clock in the system
prompt would silently invalidate the cache on every single request and multiply the session
cost. `SystemPromptBuilder` has no access to the current time, deliberately.

### Cost: images accumulating

This is the one that actually bites. A capture is up to ~4,800 image tokens. At a two-minute
cadence, a three-hour session is ninety of them — over 400k tokens replayed every turn if we
keep them all.

So we don't. **Old images are dropped from history; the text stays.** After a turn is a few
snapshots old, its image is replaced by the structured observation we extracted from it:

```
user: [image]                          → user: (snapshot 14, 21:04) gold 1.24M, Sollant 88k,
                                               chest T2 +7, weapon T2 +9 …
```

Nothing is actually lost. The image was only ever a way to *obtain* those facts, and the model
already read them once. Keeping the pixels around a second time is paying twice for the same
information.

The result is a conversation where the assistant genuinely remembers the whole session, but
only the last few turns carry image payloads.

### Backstop: compaction

For very long sessions the text alone eventually grows past what's sensible. Anthropic's
server-side compaction (beta `compact-2026-01-12`) handles that — it summarises the early
conversation in place. The one rule that matters: **append the whole `response.content` back
into history, not just the text**, or the compaction blocks are lost and the feature silently
stops working. `ConversationHistory.AppendAssistantTurn` takes the raw content list for
exactly this reason.

## Budget

Because a conversation costs more than a one-shot, the running cost estimate and the monthly
ceiling in settings matter more, not less. The session tracks cumulative spend and stops
analysing at the ceiling rather than quietly continuing.

## What resets it

A new session starts when the app starts, when the user changes the target build, or when the
user asks for a fresh start. Changing the build mid-session would invalidate the cached prefix
anyway, so it is a natural boundary.
