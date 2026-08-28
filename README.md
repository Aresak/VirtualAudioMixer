# VAM — Virtual Audio Mixer

**Open source live audio mixer for streaming and conference rooms** — unlimited
physical and virtual I/O, a gain-sharing automixer, composable DSP chains, and
remote control from a tablet or a second machine.

> **Status: design, not software.** There is no implementation yet. What exists is
> a feature catalogue, a set of architecture decisions and a complete clickable UI
> mockup. Everything below describes intent.

## Why

The closest existing tool is VoiceMeeter: closed source, a fixed number of strips,
one monitoring output, no real automixer. The individual pieces all exist in open
source — denoising, codecs, the gain-sharing automixing principle, virtual audio
endpoints — but on Windows nobody has assembled them into one mixer.

Three things are missing from every option:

- a **gain-sharing automixer**, so a room full of open microphones is not a room
  full of summed noise
- **several independent monitor buses**, not one "monitor of the master"
- a **remote client that controls the mixer and carries a monitor feed back**

VAM started for a city council livestream — a dozen people sharing three
microphones, several hours, one operator who is also cutting cameras. That is the
hardest test rather than the only target.

## What it should do

- Any number of inputs and outputs, physical or virtual, on devices that do not
  share a clock
- Per-channel processing as a chain you compose and reorder, extensible with your
  own modifiers
- A gain-sharing automixer you can watch working, and switch off in one click
- Any number of buses, including mix-minus returns and independent headphone feeds
- Multitrack recording of the raw inputs, so a bad session is still recoverable
- Remote control and remote monitoring over the network
- Diagnostics that make clock drift, dropouts and DSP cost visible before they
  become audible

## Design

A **headless engine** behind a versioned protocol, with a **native client**. The
local UI is a client of the same protocol as a tablet across the room, so remote
control is the same mechanism rather than a second one.

Written in C#. The audio path allocates nothing, locks nothing and waits for
nothing — a garbage collection pause during a live broadcast is a dropout.

The reasoning behind each choice, including the ones that were reversed, is
recorded rather than assumed. Ask in an issue if something looks wrong; it probably
has an argument behind it, and the argument might be bad.

## The mockup

[`_MockUp/vam-console.html`](_MockUp/vam-console.html) — one self-contained file,
no build step. Open it in a browser: every screen, realistic data, live meters, a
working automixer visualisation, drag-to-reorder modifier chains.

There is a **Feature IDs** toggle that stamps catalogue IDs onto the components.

It is a drawing of a native application that happens to be drawn in HTML.

## Platforms

Windows first. macOS planned. No native Linux client for now, though a browser
client is a plausible route there.

## Licence

Three licences, chosen per component — the full map is in
[LICENSING.md](LICENSING.md).

- **Engine** — [AGPL-3.0-or-later](LICENSE). Modify VAM, offer it as a service,
  publish the changes.
- **Signal modifiers** — AGPL plus a linking exception, so your modifier can carry
  any licence you like.
- **UI library and client** — [MPL-2.0](licenses/MPL-2.0.txt).
- **Protocol and client SDK** — [Apache-2.0](licenses/Apache-2.0.txt), so anyone
  can build a client or a control surface on their own terms.

Using VAM to mix audio carries no obligations at all.

## Contributing

Opinions on the design are worth more than code right now — see
[CONTRIBUTING.md](CONTRIBUTING.md). One rule is not negotiable: nothing in the
audio path allocates, locks or waits.

## Supporting it

If VAM saves you buying a mixer, [buy me a coffee](https://buymeacoffee.com/aresak).
It keeps the project maintained; it does not buy features or priority, and
everything is public either way.
