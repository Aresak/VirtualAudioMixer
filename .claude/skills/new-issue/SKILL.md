---
name: new-issue
description: Opens a GitHub issue on this repository — a bug, a task, or a follow-up found while working — after checking that it is not already filed. Use every time you are about to run `gh issue create` here, including follow-ups written down at the end of a task.
---

# Opening an issue

A task is a GitHub issue, and several sessions work this repository at once. An issue opened twice
gets fixed twice, in two pull requests that conflict. It happened on 27 October: #74 duplicated #47,
and PR #71 duplicated #70.

**The duplicate check comes first, before the issue is written.** Writing it first is how you end up
defending the draft instead of looking honestly for the one that already exists.

## 1. Look for it

Do all four. A title search alone is not enough: #47 ("Commands rewrite the graph from gRPC threads
while the control loop pumps it") and #74 ("The console's configuration is changed from two threads
at once") describe the same defect and share almost no words.

**a. Read every title.** Open and closed, so a problem fixed last month is seen as fixed:

```bash
gh issue list --state all --limit 300 --json number,state,title,labels -q '.[] | "\(.number)\t\(.state)\t\(.title)"'
```

Read all of them in the same area, not only the ones that look alike.

**b. Search by the defect, not by your wording.** The class or method involved, the symptom an
operator sees, the threads, the log line. Several searches, each short, each a different angle:

```bash
gh issue list --state all --search "<term>" --json number,state,title -q '.[] | "\(.number)\t\(.state)\t\(.title)"'
```

These help, but they do not replace (a). Searched with #74's own words — `GraphConfig`,
"configuration two threads" — GitHub does not return #47. Only reading the titles found it.

**c. Search pull requests too.** A fix may be in flight with no issue of its own, or listed under
another one's "Outside the fence":

```bash
gh pr list --state all --search "<term>" --json number,state,title -q '.[] | "\(.number)\t\(.state)\t\(.title)"'
```

**d. Read the body of every candidate.** `gh issue view <n>`. Two issues are the same when they
describe the same defect — the same code, the same cause — whatever their titles say.

## 2. Decide

| What you found | What to do |
|---|---|
| An open issue covers the defect | Do not open one. Comment on it with whatever is new: evidence, a log line, a code path it does not mention. |
| A closed issue covers it, and the problem is back | Open a new one that says it came back and links the old one. Do not reopen silently. |
| An issue overlaps, but the defect is different | Open the new one, and name the other in its `Not in scope`. |
| Nothing | Open it. |

Say which of these it was, and which issues you compared, in the message to the user. "I searched and
found nothing" without the numbers looked at is not a check anybody can trust.

## 3. Write it

The shape every issue here has:

```markdown
## Problem
What is wrong, where in the code, and how it was seen. Evidence: a log line, a file, a date.

## Scope
What to build. Where a choice is genuinely open, say "decide in the task".

## Not in scope
The fence. Name the neighbouring issues by number.

## Acceptance
How a reviewer knows it is done. Testable, and tested with `NullAudioBackend` where it can be.

## Notes
`needs-hardware`: yes or no, and why.
```

- **Title:** the behaviour that should be true, stated plainly. "An absent device is not a stream of
  dropouts", not "Fix dropout bug".
- **Labels:** from the existing set only (`gh label list`). `bug` or `task`, plus one or more `area:*`.
- **Issues opened together** reference each other. Open both, then edit each body to put in the
  other's number.

## 4. Open it

Write calls need the repository owner's token. The active `gh` account can read but gets 403 on
writes, and other sessions depend on which account is active, so never `gh auth switch`:

```bash
GH_TOKEN=$(gh auth token -u Aresak) gh issue create --title "…" --label bug --label area:engine --body-file issue.md
```

Write the body to a file in your scratchpad rather than passing it inline. A body with backticks and
`$` in it does not survive a shell quote.

## 5. Opened by accident twice anyway

Close the newer one as a duplicate, with a comment naming the older one, after copying anything only
the newer one says onto the older one.
