# Working on VAM

Rules for anyone working in this repository, human or agent. `CLAUDE.md` points here; this file is canonical.

## The two rules that are not negotiable

**1. Nothing in the audio path allocates, locks, or waits.**

No `new`, no LINQ, no strings, no boxing, no closures, no `async`, no locks, no blocking calls inside an audio
callback or anywhere in the mix graph. Buffers are pooled and pre-allocated when the graph is built.
Parameter changes are published as immutable snapshots swapped in with one interlocked write; the audio thread
only ever reads.

This is enforced by a test, not by good intentions. See `docs/audio-path.md` for where the boundary is.

A garbage collection pause during a three-hour live broadcast is a dropout, and the person it happens to
cannot start the meeting again.

**2. One task, one branch, one pull request.**

Never commit to `main`. Ever. Not for a typo.

## The task workflow

Work is tracked as tasks with stable IDs — `VAM-001`, `VAM-002`. Each has a scope, an explicit
**out of scope**, acceptance criteria and a verification method.

```
1. Pick a task that is Ready. Never one that is only in Backlog.
2. git switch -c task/VAM-001-short-slug
3. Implement exactly what the task says. Nothing else.
4. Run the tests. All of them, not the ones you think are relevant.
5. Commit with a short, plain message. Sign off: git commit -s
6. Push the branch, open a pull request.
7. The pull request gets reviewed before it merges. Always.
```

### Write the reasoning down, in the vault

Code and commits record what changed. They are a poor place for **why a decision went the way it did**,
because the next person asking is not reading a diff — they are asking whether to reverse it.

That belongs in the Obsidian vault, under `Projects/Virtual Audio Mixer/`:

| Folder | What goes there |
|---|---|
| `Decisions/` | One note per decision that reverses something written down, or that a future session would otherwise re-litigate. Add a row to `Decisions/_Decisions.md`. |
| `_AI/` | Build logs, working notes and context a long session needs to survive its own summarisation. |

**Write it as it happens, not at the end.** A session's context gets summarised when it grows, and a
decision made an hour ago is exactly what the summary drops. A working note in `_AI/` is where a long
run keeps what it must not lose.

Until this section existed the rule lived only in the vault, which is the one place a session that
reads the repository never looks — so it was followed by whoever remembered and nobody else.

### The fence

A task's **Not in scope** section is binding. If you find a real problem outside it — and you will — open an
issue or write it down. Do not fix it in this branch.

This is the rule that keeps a two-hour task from becoming a four-hour one that touches nine files and cannot
be reviewed.

### When a task cannot be finished

Some tasks are marked `needs-hardware`. They cannot be verified without the real devices in the real room —
measuring drift, judging whether denoise sounds right, confirming a microphone re-enumerates cleanly.

Implement them, then **stop**. Say what remains. Do not mark them done.

The same goes for anything where the task turns out to be wrong, ambiguous in a way that matters, or blocked
by something nobody noticed. Stopping and saying so is a good outcome. Guessing is not.

## Commits

Short and plain. A one-line subject, and at most a couple of matter-of-fact lines of body if the subject does
not carry it.

**No AI attribution of any kind.** No `Co-Authored-By` for a tool, no "generated with", nothing.

Sign off every commit with `-s`. There is no CLA; the sign-off is the only record that the change was offered
under the file's licence.

## Pull requests

One task per pull request. The description says which task, and how each acceptance criterion was met.

If the diff touches something the task did not mention, the pull request explains why — or the change comes
out.

## Style

Match the code around you.

Analyzers are wanted, but only the ones that catch defects. The build treats warnings as errors and runs
Roslyn's code-style analyzers and `Microsoft.VisualStudio.Threading.Analyzers`, which catches sync-over-async,
`.Result` and blocking calls. In a project whose first rule is that nothing waits, that is a second enforcement
mechanism for rule 1 rather than a tidiness exercise. Warnings-as-errors has already earned its place: CA1416 is
what forced the Windows device layer into its own project instead of letting it fail somewhere later.

No analyzer whose job is taste. Naming, spacing and ordering live in `.claude/skills/csharp-code-writer` and
`.claude/skills/razor-markup-formatting`, where a person decides. Do not add an analyzer because it feels tidy.
Add one because it catches a bug you can name.

## Testing

- Unit tests run by default. Long-running tests are a separate category CI does not run
- Anything touching the device layer or the mix graph needs a soak test, not a unit test. Clock drift between
  free-running USB devices does not show up in five minutes; it shows up in hour three, as a click, during a
  council meeting
- A test that fails at random is worse than no test. It gets disabled within a week and then nothing is
  protecting anything

## Licensing

Three licences, per component — see `LICENSING.md`. Every project directory carries its own `LICENSE`. Do not
move code between projects without checking what that does to its licence.

## Signal modifiers

Modifiers are the extension point. Writing one does not require contributing it upstream, and a modifier that
talks to VAM only through the modifier API can carry any licence — see the exception at the top of `LICENSE`.

Inside this repository, a modifier is still bound by rule 1.
