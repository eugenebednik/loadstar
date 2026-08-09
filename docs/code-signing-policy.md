# Code signing policy

Who can change Loadstar's code, who can authorize a signature, and how a released artifact is
produced. This page exists because a signature is only worth the process behind it — it proves an
artifact came from this pipeline, not that the pipeline is careful.

## Project

- **Repository:** <https://github.com/eugenebednik/loadstar> — the only source of Loadstar. There is
  no mirror, and no installer is distributed anywhere else.
- **License:** [MIT](../LICENSE), for every component.
- **Artifacts to be signed:** `Loadstar.exe` and the per-language
  `Loadstar-<version>-x64-<lang>.msi` installers.

## Roles

Loadstar is a single-maintainer project. This section says so plainly rather than describing a
separation of duties that does not exist.

| Role | Held by | Responsibility |
| --- | --- | --- |
| Author | R3N | Writes and commits code |
| Reviewer | R3N | Reviews changes before they reach `main` |
| Approver | R3N | Authorizes signing of a release artifact |

**One person currently holds all three roles**, so the integrity guarantees rest on the automated
controls below rather than on review by a second party. If a maintainer joins, Reviewer and Approver
move to them and this table changes in the same commit.

## Controls

- **Multi-factor authentication** is required on the GitHub account and on the signing service.
- **Every artifact is built by CI from public source.** Nothing is built on a developer machine and
  uploaded by hand. The workflow is [.github/workflows/build.yml](../.github/workflows/build.yml),
  and each release records the commit it was built from.
- **The test suite gates every publish.** Both publishing jobs declare `needs: test`, so a failing
  build or a failing test cannot produce a downloadable artifact.
- **The anti-cheat posture is machine-checked, inside that same gate.** `AntiCheatPostureTests`
  scans the compiled assemblies for injection, process-memory and synthetic-input APIs and fails the
  build if it finds any. See [anti-cheat-posture.md](anti-cheat-posture.md) for the full contract.
  This is the control most relevant to anyone assessing whether Loadstar is unwanted software: the
  project's central constraint is enforced by a test, not by a promise.
- **Pinned releases come only from tags.** A `v*` tag is the only thing that produces one.
- **Product name and version are set centrally** in `Directory.Build.props` and stamped by CI, so
  artifact metadata cannot drift per-build.

## Signing status

Loadstar releases are **not currently signed**, and the README says so where users will meet the
Windows warning.

An application to the [SignPath Foundation](https://signpath.org/), which provides free code signing
to open-source projects, is pending. If accepted, signing will be performed by
[SignPath.io](https://about.signpath.io/) using a certificate issued to the SignPath Foundation,
with the private key held in SignPath's HSM and never in this project's possession. The Approver
above authorizes each signed release. This page and the README will be updated at that point.

## Reporting a problem

Open an issue at <https://github.com/eugenebednik/loadstar/issues>. If you believe you have found a
security or supply-chain problem, say so in the title and it will be handled ahead of feature work.
