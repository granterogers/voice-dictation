# Claude Project Prompt — VoiceDictation

> **This file is no longer the instruction set. See [`AGENTS.md`](AGENTS.md).**

## Why this file is a stub

This document used to be a near-duplicate of `AGENTS.md`, and both described a workflow
that had stopped being used: Claude editing source, generating a `deploy.py` with the
source base64-embedded, the user double-clicking it to build and test, and pushing via the
GitHub Contents API.

That workflow is dead. The project is developed with ordinary git — edit, build, test,
`git commit`, `git push origin main` — with versions tagged automatically by a push hook.

Having the same workflow documented in two places, neither matching what actually
happened, caused real confusion and wasted debugging effort. So there is now exactly one
instruction set.

## Where things went

| Old section | Now |
|---|---|
| Project overview, stack, paths | `AGENTS.md` §1 |
| How to make changes | `AGENTS.md` §2 — git, not a deploy script |
| Build commands | `AGENTS.md` §3 — note the mandatory `--source` flags |
| Deploy script rules and template | **Removed.** See `AGENTS.md` §7 |
| Key app architecture | `AGENTS.md` §8 and `HANDOVER.md` §12 |
| Known past mistakes | `AGENTS.md` §5, §8 |

## If you are starting a fresh session

Read `AGENTS.md` in full, then `HANDOVER.md` for engineering history. You do not need to
paste source files into the conversation — the working copy is the source of truth, and
GitHub holds the canonical history.

One rule worth repeating here, because the old version of this file is what caused the
breach: **never embed a credential in a file in this repo.** It is public. The old deploy
template substituted a real GitHub token into a generated script, and that script was
committed. See `AGENTS.md` §5.
