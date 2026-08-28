---
description: Implement one VAM task end to end — branch, implement, test, review, pull request.
argument-hint: VAM-001 [paste the task definition if it is not in the repo]
---

Implement task **$1** in this repository.

Read `AGENTS.md` first. It is binding, including the parts you disagree with.

## The task definition

If the definition of $1 was not given to you, **stop and ask for it.** Do not infer a task from its ID, from
the epic, or from what seems sensible. A guessed task produces a pull request nobody can review against
anything.

## Steps

**1. Branch.** `git switch -c task/$1-<short-slug>` from an up-to-date `main`. Never work on `main`.

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

- which task, and a one-line restatement of what it was for
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
