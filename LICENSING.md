# Licensing

VAM is open source under three licences, chosen per component rather than one for
everything. This page is the map. The short version:

| Part of the repository | Licence | Why |
|---|---|---|
| **Engine and everything not listed below** | [AGPL-3.0-or-later](LICENSE) | The engine is where the value is. AGPL is the only common licence that cares about running software over a network: modify VAM and offer it as a service, and you publish the changes. |
| **Signal modifier API and modifiers written against it** | AGPL-3.0-or-later **plus a linking exception** — see the top of [LICENSE](LICENSE) | The point of the modifier API is that other people write modifiers. Without the exception, a plugin loaded into the process would be a derivative work and would have to be AGPL too. |
| **Shared UI library and client application** | [MPL-2.0](licenses/MPL-2.0.txt) | File-level copyleft: improvements to the UI come back, but the licence does not conflict with mobile app store terms the way AGPL does. |
| **Protocol contracts (`.proto`) and the generated client SDK** | [Apache-2.0](licenses/Apache-2.0.txt) | Anybody should be able to write a client, a control surface or a bridge without inheriting copyleft. A Stream Deck plugin, a hardware desk, someone else's app — all welcome, on their own terms. |

Every project directory carries its own `LICENSE` file once it exists. Until then
the texts live in [`licenses/`](licenses/).

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
