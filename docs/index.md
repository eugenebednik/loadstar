# Loadstar

An AI game companion overlay for Windows. It watches your screen, compares what it sees against the
build you are aiming for, and tells you the highest-value thing to do next with the resources you
actually have. First supported game: Throne and Liberty.

Loadstar never touches the game — no injection, no renderer hooking, no process memory, no synthetic
input. It captures a window the way OBS does and draws its own separate window on top.

- **[Privacy policy](privacy.md)** — what is captured, what leaves your machine, and where it goes
- **[Anti-cheat posture](anti-cheat-posture.md)** — the design constraints, and why they are absolute
- **[Code signing policy](code-signing-policy.md)** — who can authorize a release, and how one is built

Source, releases and issues: **[github.com/eugenebednik/loadstar](https://github.com/eugenebednik/loadstar)**
