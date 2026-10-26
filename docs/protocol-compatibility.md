# Protocol compatibility

The console and the engine are two processes and often two machines, and they are not updated
together. A console newer than the engine it is pointed at is a normal state, not an edge case.

## The rules

**A breaking change bumps `protocol_version`.** Removing or renumbering a field, changing what a field
means, or changing what a command does. `Hello` then refuses the console with a sentence, and that is
the right outcome: an operator can act on "the console is newer than the engine", not on a crash.

**An addition gets a name.** A new field, message, command or method is added to
`src/Vam.Protocol/EngineFeatures.cs` and listed in `EngineFeatures.All`. The engine sends the list in
`HelloReply.features`; a test fails if a name is defined but not sent.

Names, not a minor version number: pull requests land here in parallel, and two of them would both take
the next number.

**The console asks before it uses an addition.** `Session.Supports(EngineFeatures.X)` decides whether
a control is shown or a method is called. When it is not, the view says the engine is older than the
console rather than drawing a default that looks like a real value - a capture switch shown off is a
claim about what is being recorded.

**A sub-message is never read through.** Protobuf has no required fields, so any message field can
arrive as null, from an older engine or a buggy one. Read `x.Captures` with `is { } captures`, never
`x.Captures.Inputs`. This is the backstop for an addition somebody forgot to name.

## The other direction

A newer engine and an older console need nothing: the console ignores fields it does not know, and an
engine refuses a command it does not know with a sentence (`MixerService.Apply`).

## Before the list

Engines built before feature names existed send an empty list. Everything named in `EngineFeatures`
was added after them, so such an engine is correctly treated as having none of it, and the status bar
says the engine is older than the console.
