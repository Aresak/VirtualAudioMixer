# Licensing

VAM is open source under three licences, chosen per component rather than one for
everything. This page is the map. The short version:

| Part of the repository | Licence | Why |
|---|---|---|
| **Engine, the Windows device layer, test utilities and engine tests** — `src/Vam.Engine`, `src/Vam.Engine.Windows`, `tests/Vam.TestKit`, `tests/Vam.Engine.Tests`, `tests/Vam.Engine.Windows.Tests` — and everything not listed below | [AGPL-3.0-or-later](LICENSE) | The engine is where the value is. AGPL is the only common licence that cares about running software over a network: modify VAM and offer it as a service, and you publish the changes. |
| **Signal modifier API and modifiers written against it** | AGPL-3.0-or-later **plus a linking exception** — see the top of [LICENSE](LICENSE) | The point of the modifier API is that other people write modifiers. Without the exception, a plugin loaded into the process would be a derivative work and would have to be AGPL too. |
| **Shared UI library and client applications** — `src/Vam.Ui`, `src/Vam.Client`, and `src/Vam.WebClient` when it exists (both are placeholder class libraries today) | [MPL-2.0](licenses/MPL-2.0.txt) | File-level copyleft: improvements to the UI come back, but the licence does not conflict with mobile app store terms the way AGPL does. |
| **Cross-cutting primitives** — `src/Vam.Core` | [Apache-2.0](licenses/Apache-2.0.txt) | It holds the exception base every other project derives from, so it is a dependency of the AGPL engine and the MPL client alike. A copyleft licence on a type that everything references would reach places the split was drawn to keep separate. It deliberately contains nothing worth protecting. |
| **Protocol contracts (`.proto`) and the generated client SDK** — `src/Vam.Protocol` | [Apache-2.0](licenses/Apache-2.0.txt) | Anybody should be able to write a client, a control surface or a bridge without inheriting copyleft. A Stream Deck plugin, a hardware desk, someone else's app — all welcome, on their own terms. |

Every project directory carries its own `LICENSE` file, copied from
[`licenses/`](licenses/):

| Project directory | `LICENSE` is a copy of |
|---|---|
| `src/Vam.Core` | [`licenses/Apache-2.0.txt`](licenses/Apache-2.0.txt) |
| `src/Vam.Engine` | the root [`LICENSE`](LICENSE) — AGPL **plus the modifier exception** |
| `src/Vam.Protocol` | [`licenses/Apache-2.0.txt`](licenses/Apache-2.0.txt) |
| `src/Vam.Ui` | [`licenses/MPL-2.0.txt`](licenses/MPL-2.0.txt) |
| `src/Vam.Client` | [`licenses/MPL-2.0.txt`](licenses/MPL-2.0.txt) |
| `src/Vam.Engine.Windows` | [`licenses/AGPL-3.0.txt`](licenses/AGPL-3.0.txt) |
| `src/Vam.Modifiers.Abstractions` | the root [`LICENSE`](LICENSE) — AGPL **plus the modifier exception** |
| `tests/Vam.TestKit` | [`licenses/AGPL-3.0.txt`](licenses/AGPL-3.0.txt) |
| `tests/Vam.Engine.Tests` | [`licenses/AGPL-3.0.txt`](licenses/AGPL-3.0.txt) |
| `tests/Vam.Engine.Windows.Tests` | [`licenses/AGPL-3.0.txt`](licenses/AGPL-3.0.txt) |

### Which projects carry the modifier exception

`src/Vam.Engine` and `src/Vam.Modifiers.Abstractions`, and both carry a byte-for-byte copy of the root
[`LICENSE`](LICENSE) rather than of `licenses/AGPL-3.0.txt`. The exception is what lets somebody
write a closed-source modifier, and a per-project `LICENSE` is the file a person actually opens
when deciding whether they are allowed to. A plain AGPL copy there would contradict the root file
and the contradiction would favour the reading that kills the plugin ecosystem.

`src/Vam.Modifiers.Abstractions` is the assembly the exception names, and it **references
nothing** — no project, no functional package. That is a licence condition rather than a
preference: condition (a) of the exception is only testable if the assembly a third party links
against is genuinely standalone, and one reference to the engine would make the permission it
grants stop meaning anything.

`src/Vam.Engine.Windows` does **not** carry it either, and the reason is the same one. It is the
WASAPI device layer; no part of the modifier API lives there, so there is no combined work for the
exception to permit. Copying it in would advertise a permission with nothing to act on, and would
suggest — wrongly — that a closed-source device backend is a thing the exception contemplates.

The test projects do **not** carry it, deliberately. `tests/Vam.TestKit` and
`tests/Vam.Engine.Tests` are plain AGPL. Nothing links against them from outside the repository,
so there is no combined work for the exception to permit — copying it there would suggest a
permission that has nothing to act on. Saying so explicitly is worth more than copying it
everywhere and hoping.

A directory added later carries a `LICENSE` too, and gets a row here.

## What this means in practice

**Using VAM to mix audio** — do whatever you like. Council meetings, streams,
podcasts, commercial work. No obligations, no attribution required in your stream.

**Writing a signal modifier** — your modifier is yours, under whatever licence you
choose, as long as it talks to VAM only through the modifier API. See the
exception at the top of [LICENSE](LICENSE) for the exact wording.

**Writing a client, a control surface or an integration** — the protocol is
Apache-2.0. Build what you want, licence it how you want.

**Modifying VAM itself and distributing it** — AGPL applies: publish your changes
under the same licence.

**Running a modified VAM as a network service others use** — AGPL section 13
applies: those users get the source of your modifications. This is the clause the
licence was chosen for.

## Contributions

Contributions are accepted under the licence of the file being changed, certified
by a `Signed-off-by` line — the [Developer Certificate of Origin](https://developercertificate.org/).
See [CONTRIBUTING.md](CONTRIBUTING.md).

There is no CLA, which is deliberate: a CLA is friction for contributors, and the
per-component split already solves the problem a CLA would have been needed for.
The consequence is worth knowing — contributed code cannot be relicensed later
without asking its authors.

## Third-party components

VAM builds on other people's work. Attributions and licences are in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

*This split was chosen with care but not by a lawyer. If VAM ever grows a
commercial edition, a hosted service, or an app store release, get the wording
reviewed by someone qualified before shipping it.*
