# Contributing

VAM is early — there is no implementation yet, only architecture decisions and a
UI mockup. The most useful contributions right now are opinions on the design,
not code.

## Before writing code

Open an issue first. The design is settled enough that a pull request against the
wrong assumption is wasted effort, and settled decisions are written down with
their reasoning so you can argue with the reasoning rather than guess at it.

## The one rule that is not negotiable

**Nothing in the audio path allocates, locks, or waits.**

That means no `new`, no LINQ, no strings, no boxing, no closures, no `async`, no
locks and no blocking calls inside an audio callback or anywhere in the mix graph.
Buffers are pooled and pre-allocated when the graph is built. Parameter changes
are published as immutable snapshots swapped in with one interlocked write; the
audio thread only ever reads.

This is enforced, not encouraged: the test suite asserts zero allocations on the
audio thread and the build fails if the number moves.

A garbage collection pause during a three-hour public broadcast is a dropout, and
the person it happens to cannot start the meeting again.

## Signal modifiers

The modifier API exists so you do not have to contribute a modifier upstream to
write one. Yours can carry any licence, provided it talks to VAM only through the
API — see the exception at the top of [LICENSE](LICENSE).

If you would like a modifier bundled with VAM, that is welcome too, and then it
follows the licensing below.

## Certificate of origin

Contributions are accepted under the licence of the file you are changing — see
[LICENSING.md](LICENSING.md), which differs by component.

Sign your commits off with the
[Developer Certificate of Origin](https://developercertificate.org/):

```
git commit -s -m "Your message"
```

That adds a `Signed-off-by` line, which certifies you wrote the change or have the
right to submit it under the file's licence.

There is no CLA. The consequence is deliberate and worth knowing: contributed code
cannot be relicensed later without asking you.

## Style

Style is enforced at build time, not left to habit. The build treats warnings as
errors (`TreatWarningsAsErrors`) and runs Roslyn's built-in code-style analyzers
as part of compilation (`EnforceCodeStyleInBuild`), configured in `.editorconfig`;
the `...Async` suffix is enforced by the Microsoft.VisualStudio.Threading
analyzers. There is no third-party style analyzer in the tree — the dedicated one
for these guidelines is no longer maintained — so the build enforces the subset it
can (explicit types over `var`, file-scoped namespaces, braces, modifier order,
accessibility, field naming, the async suffix) and the rest is convention: write
C# to the [C# Coding Guidelines](https://csharpcodingguidelines.com/) and match the
code around you.

The conventions themselves are written down in `.claude/skills/csharp-code-writer`
and `.claude/skills/razor-markup-formatting`, so they are one document rather than
folklore.

The audio-path rule above remains the only hard *behavioural* constraint; the
style rules here are about form. Three of the C# conventions are inverted below
that line — returning collection interfaces, avoiding nulls via LINQ, and avoiding
`ref`/`out` — because each of them allocates. The skill says which and why.

## Testing

Run the suite:

```
dotnet test
```

That runs the unit tests and skips the long-running ones. Run those too:

```
VAM_LONGRUNNING=1 dotnet test
```

or, in PowerShell:

```
$env:VAM_LONGRUNNING = "1"; dotnet test
```

Long-running tests opt in through an environment variable rather than a runner
filter, so a plain `dotnet test` behaves the same way on your machine as it does
in CI. Mark them with `[Trait("Category", TestCategories.LongRunning)]` and
`SkipUnless = nameof(LongRunningTests.IsEnabled)`; there is a worked example in
`tests/Vam.Engine.Tests/Harness/LongRunningCategoryTests.cs`.

Anything touching the device layer or the mix graph needs a soak test, not a unit
test. Clock drift between free-running USB devices does not show up in five
minutes; it shows up in hour three, as a click, during a council meeting. The
engine has a soak mode that drives it from a file faster than realtime — use it,
and report what it did.

### The allocation gate

Where the boundary is drawn — what counts as the audio path and what does not — is
[`docs/audio-path.md`](docs/audio-path.md). Read it before writing anything that runs
on the mix thread or a device thread; it decides several cases that look obvious and
are not.


`AllocationAssert` in `tests/Vam.TestKit/Allocations/AllocationAssert.cs` asserts
that a region of code allocates nothing. Use the closure-free overload — the one
taking a state argument and a `static` lambda — everywhere: a capturing lambda
allocates its closure, and then the harness is measuring itself.

```csharp
AllocationAssert.None(buffer, static samples =>
{
    Span<float> block = samples.AsSpan();

    for (int index = 0; index < block.Length; index++)
    {
        block[index] = (block[index] * 0.5f) + 0.25f;
    }
});
```

The gate is only worth something if it fires. `tests/Vam.Engine.Tests/Allocations/AllocationGateProofTests.cs`
allocates on purpose inside the gate and asserts that it throws — that test is
intentional and must not be "fixed". Replacing the body of `AllocationAssert.None`
with an empty method turns it red, along with three others.
