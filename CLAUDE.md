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
| Audio path boundary | `docs/audio-path.md` |
| Task definitions | GitHub issues on this repository, and the vault. Ask before starting a task you cannot read |

## Project layout

This table describes the projects **as they are today**, not as they will be. A row is updated
when the project changes shape, so a row can be trusted rather than read as intent.

| Project | Contains today | Becomes | Licence |
|---|---|---|---|
| `src/Vam.Core` | `VamException`, the base for VAM's own exceptions | Cross-cutting primitives everything shares | Apache-2.0 |
| `src/Vam.Engine` | The device abstraction, ring buffer, device registry, drift estimator, resampler, fill servo and per-device input channel. Platform-free, and a test enforces it | The mix graph and DSP, in EPIC-03 onwards | AGPL-3.0-or-later **plus the modifier exception** |
| `src/Vam.Engine.Windows` | WASAPI capture and render, driving the clients directly, plus device notifications | Later ASIO and RNNoise interop | AGPL-3.0-or-later |
| `src/Vam.Modifiers.Abstractions` | The modifier ABI: `Modifier`, its context, its descriptors. **References nothing** | The same, permanently — every addition is a rebuild for every existing modifier | AGPL-3.0-or-later **plus the modifier exception** |
| `src/Vam.Protocol` | The `.proto` contracts, the generated gRPC client and server, and the meter frame codec | The same, versioned | Apache-2.0 |
| `src/Vam.Server` | The headless engine host and its gRPC service. Owns the devices, the graph, the clock and the recording | The same, plus whatever EPIC-12 adds | AGPL-3.0-or-later |
| `src/Vam.Ui` | The whole console — every view, component, the session client, the startup connector and the two string files. A host contributes nothing but a startup file | The same, plus whatever later epics add to the console | MPL-2.0 |
| `src/Vam.Client` | The MAUI Blazor Hybrid host: a startup file, its platform services and the engine launcher. A `.razor` file here is a defect | The same | MPL-2.0 |
| `src/Vam.WebClient` | The Blazor Server host, and the same three files for a browser. It cannot start an engine and says so | The same | MPL-2.0 |
| `tests/Vam.TestKit` | `AllocationAssert`, the test category policy, `NullAudioBackend`, the drift simulation and a recording logger | The full soak fixtures, in EPIC-12 | AGPL-3.0-or-later |
| `tests/Vam.Engine.Tests` | The allocation gate and its proof, the device-layer suite and the eight-hour drift soak | DSP and automix tests | AGPL-3.0-or-later |
| `tests/Vam.Engine.Windows.Tests` | Sample-format conversion, and the capture and render tests that need real devices | The hotplug tests | AGPL-3.0-or-later |
| `tests/Vam.Server.Tests` | EPIC-08's gate: the whole mixer driven over real gRPC with no UI | Protocol and persistence tests | AGPL-3.0-or-later |
| `tests/Vam.Ui.Tests` | The console's own logic, not its components: the localiser, the fader scale, engine address parsing | More of the same | MPL-2.0 |

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
