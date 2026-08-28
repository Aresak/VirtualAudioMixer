# CLAUDE.md

**Read [AGENTS.md](AGENTS.md) before doing anything here.** It is the canonical set of rules and this file
only repeats what must never be missed.

## Never

- **Commit to `main`.** One task, one branch (`task/VAM-nnn-slug`), one pull request. No exceptions, not for
  a typo
- **Allocate, lock or wait in the audio path.** See `docs/audio-path.md`. Enforced by a test
- **Mention Claude or AI in a commit message.** No `Co-Authored-By` for a tool, no "generated with"
- **Merge your own pull request.** Every pull request is reviewed first — use the `pr-reviewer` agent
- **Work outside a task's `Not in scope` section.** Found something? Write it down, do not fix it here

## Always

- `git commit -s` — the sign-off is the only licence record; there is no CLA
- Short, plain commit messages
- Run the whole test suite before opening a pull request, not the part that seems relevant
- Stop and say so when a task is `needs-hardware`, wrong, or blocked. Guessing is worse than stopping

## Where things are

| | |
|---|---|
| Rules | `AGENTS.md` |
| Licence map | `LICENSING.md` |
| UI mockup — the design of record | `_MockUp/vam-console.html` |
| Audio path boundary | `docs/audio-path.md` (created by VAM-008) |
| Task definitions | Not in this repository yet. Ask before starting a task you cannot read |

## Project layout

| Project | Contains | Licence |
|---|---|---|
| `src/Vam.Engine` | Audio engine, graph, devices, DSP | AGPL-3.0-or-later |
| `src/Vam.Protocol` | `.proto` contracts and generated code | Apache-2.0 |
| `src/Vam.Ui` | Shared Razor components | MPL-2.0 |
| `src/Vam.Client` | MAUI Blazor Hybrid host | MPL-2.0 |
| `tests/Vam.TestKit` | Test utilities, including the allocation assertion | AGPL-3.0-or-later |
| `tests/Vam.Engine.Tests` | Engine tests | AGPL-3.0-or-later |

## Commands

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet test --filter Category=longrunning
```
