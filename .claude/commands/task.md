---
description: Implement one VAM task end to end — branch, implement, test, review, pull request.
argument-hint: <issue number, e.g. 42> [paste the task definition if it is not in the repo]
---

Implement task **#$1** in this repository. A task is a GitHub issue; $1 is its number.

Read `AGENTS.md` first. It is binding, including the parts you disagree with.

## The task definition

Read it with `gh issue view $1`. If you cannot read it and it was not given to you, **stop and ask for it.** Do not infer a task from its ID, from
the epic, or from what seems sensible. A guessed task produces a pull request nobody can review against
anything.

## Steps

**1. Check nobody has done it, then branch.** Several sessions work this repository at once. Fetch,
then ask GitHub three things: whether the issue is still open, which pull requests say they close it,
and which pull requests or branches are named for it.

```bash
git fetch origin
gh issue view $1 --json state,closedByPullRequestsReferences
gh pr list --state all --limit 1000 --json number,state,headRefName -q '.[] | select(.headRefName | startswith("task/'$1'-"))'
git branch -r --list "origin/task/$1-*"
```

Not a text search for the number: `64 in:body` also finds a pull request that mentions "16–64 px".
The first query catches a pull request that says `Closes #$1`; the other two catch one that does not.

If the issue is closed, or a pull request is open or merged, **stop and say so.** Do not build a
second one. PR #71 duplicated the
already-merged #70 this way, from a `main` fetched an hour too early.

Then `git switch -c task/$1-<short-slug> origin/main`. Never work on `main`.

Anything found along the way that becomes an issue is opened with the `new-issue` skill.

**2. Implement what the task says.** Its **What to build** section is the instruction; its **Constraints** are
what must still hold afterwards.

The **Not in scope** section is a fence, not a suggestion. If you find a real problem outside it, write it
down at the end of your report and leave it alone. Do not fix it here, however small it looks.

Where the task says a choice is open — "decide as part of this task" — decide it, and say in the pull request
what you chose and why.

**3. Test.** Run the whole suite, not the part that seems relevant.

```bash
dotnet test
```

If the task's verification calls for something the tests cannot do — a soak run, a listening check, real
hardware — do not pretend. Say what remains unverified.

**4. Commit.** Short, plain messages. `git commit -s`. No AI attribution anywhere, ever.

**5. Review.** Before opening the pull request, hand the branch to the **pr-reviewer** agent with the task ID
and the task definition. Act on what it finds, or say why a finding does not apply. Do not skip this because
the change is small.

**6. Pull request.** Push the branch and open it with `gh pr create`. The description must contain:

- `Closes #$1`, and a one-line restatement of what it was for
- each acceptance criterion with how it was met, or why it could not be
- what you ran and what it printed
- any decision you made that the task left open
- anything you found outside the fence and deliberately did not touch

**Do not merge it.** Leave it open.

## When to stop

Stop and report rather than proceeding if:

- the task is `needs-hardware` and you have reached the point where hardware is required
- the task contradicts `AGENTS.md`, `LICENSING.md`, or a decision recorded in the repository — those win, and
  the task needs correcting
- the task turns out to be ambiguous in a way that changes what gets built
- something is blocked by work that is not finished

Stopping and saying why is a good outcome. A pull request built on a guess is not.
