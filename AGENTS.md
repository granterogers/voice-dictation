# AGENTS.md — VoiceDictation

> **This file is the canonical instruction set for AI assistants working on this project.**
> Read it in full before making any changes. Where anything else in this repo disagrees
> with this file, **this file wins** — see §7 for documents that are deliberately stale.
>
> For the detailed engineering history (root causes, verification techniques, bugs that
> were invisible from reading the code), see `HANDOVER.md` — especially §9 and §12.

---

## 1. What this is

A Windows system-tray app: C# / .NET 8 / Windows Forms, essentially one source file.

Press **Win+\\** or click the tray icon, speak, and the app transcribes via Groq Whisper,
cleans the text up via a Groq chat model, and pastes the result at the cursor.

| | |
|---|---|
| Repo | `github.com/granterogers/voice-dictation` — **PUBLIC** |
| Working copy | `C:\Users\grant.rogers\My Apps\DEV\voice-dictation` |
| Source | `voice-dictation-program.cs`, `voice-dictation.csproj` |
| Build output | `bin\Release\net8.0-windows\win-x64\publish\VoiceDictation.exe` |
| Stack | .NET 8 / WinForms / NAudio 2.2.1 / Groq API |

---

## 2. Workflow

**Git is the workflow.** Edit the source in the working copy, build, test, commit, push.
There is no deploy script, no base64 bundling, and no GitHub Contents API — see §7.

1. Edit `voice-dictation-program.cs` directly.
2. Build and relaunch (§3). The user tests the running app.
3. `git add` / `git commit` / `git push origin main` once the user asks you to.
4. **Rebuild and relaunch again after pushing** — see §4.

### Rules

- **Commit and push only when the user asks.** They test first.
- Work on `main`. That is what this repo does; do not open branches or PRs unprompted.
- **Prefer the PowerShell tool for `git push`.** The Bash tool's `git push` has returned a
  silent exit 128 in this environment, repeatedly, for reasons never diagnosed.
- PowerShell cannot pipe a heredoc into `git commit -F -`. Write a long commit message to
  a file in the scratchpad and pass the path.

---

## 3. Build and run

```powershell
# Kill the running instance first: the single-instance mutex makes a second copy exit
# silently, and the running exe locks the file that publish needs to overwrite.
Get-Process -Name VoiceDictation -ErrorAction SilentlyContinue |
  Stop-Process -Force -ErrorAction SilentlyContinue

dotnet publish "voice-dictation.csproj" -c Release -r win-x64 `
  --source "https://api.nuget.org/v3/index.json" `
  --source "C:\Users\grant.rogers\.nuget\packages"

Start-Process "bin\Release\net8.0-windows\win-x64\publish\VoiceDictation.exe"
```

**The `--source` flags are mandatory.** `NuGet.config` pins package sources to the local
cache only, which the user has chosen to keep. Without the flags the restore fails with
`NU1102` whenever the SDK wants a runtime pack newer than the cache holds. Do not "fix"
this by deleting `NuGet.config` — that decision has been made.

A full publish is slow (~2-5 min, self-contained single file ~155 MB). Give the tool a
generous timeout. The build ends with `Build version: vX.Y.Z`.

**Kill and relaunch without asking.** This is a standing instruction from the user.

---

## 4. Versioning

Versions are tagged by a **global `PostToolUse` hook** at
`~/.claude/hooks/version-on-push.ps1`, outside this repo. The first push to a repo
creates `v1.0.0`; every push after bumps the **minor** version (`v1.1.0`, `v1.2.0`, …).

- **Never create, bump or push a tag by hand** — that double-tags the repo.
- If the hook's `Successful push to version vX.Y.Z` confirmation does not appear, verify
  with `git tag --points-at HEAD` and `git ls-remote --tags origin refs/tags/<tag>`.
  Note `ls-remote` sorts **lexically**, so `v1.9.0` sorts after `v1.47.0` — querying the
  specific ref is the only reliable check.
- `voice-dictation.csproj` reads the tag at build time into `InformationalVersion`, so a
  build made *before* pushing bakes in the *previous* tag. **Always rebuild and relaunch
  immediately after a push** so the running app's version never lags the tag.

---

## 5. Credentials and secrets

**This repository is public.** Treat everything committed as published forever.

- **Never put a credential in any file in the repo** — not a token, not a key, not as a
  placeholder that later gets substituted, not inside a generated script. A GitHub token
  reached this public repo exactly that way once already (§7).
- The Groq key lives in `groq_key.txt` next to the exe (inside the ignored `bin/` tree).
  Never print it, log it, or commit it. Assume it is already there; do not ask.
- The trace log (§6) deliberately records transcript text but never the key. Keep it that
  way when adding log lines.

### Never commit

| Path | Why |
|---|---|
| `bin/`, `obj/` | Build output |
| `groq_key.txt` | Live credential |
| `voice-dictation.log` | Local trace, contains dictated text |
| `sounds/` | **Anthropic-owned audio** extracted from the installed Claude desktop app for local use. Redistributing it from a public repo is not ours to do. |

All four are in `.gitignore`. `AppSettings.ResolveSound()` falls back to an all-Windows
scheme when `sounds/` is absent, so a fresh clone works without it.

---

## 6. Debugging: read the log first

`voice-dictation.log`, next to the exe, records each cycle: `=== CYCLE START/END ===`,
every sound with the line number that triggered it, the capture mode, and
`RAW` → `POLISHED` → `FORMATTED` as JSON-escaped strings.

This log is the single highest-value debugging tool in the project. It has already
converted two separate "mystery sound" reports into a one-line root cause. **Reach for it
before theorising.**

Two classes of bug in this codebase are invisible from reading the source:

- **Silently swallowed exceptions.** `Polish()` degrades to raw transcript text on
  failure, so a totally dead API surfaced only as an unexpected sound. When you add a
  `catch`, log it.
- **OS-level state.** Hotkey registration and window z-order fail in ways no amount of
  code-reading reveals. Verify empirically — `HANDOVER.md` §9.1 documents the
  `RegisterHotKey` probe technique used to pin one such bug.

---

## 7. Deliberately stale files — do not follow or revive

- **`deploy.py`** — a generated artifact from an abandoned workflow (build to temp, prompt
  `Happy with it? Y/N`, install over a copy in `My Apps\VoiceDictation`, push via the
  GitHub Contents API). **It is not part of the build or release process. Do not run it,
  regenerate it, or treat it as a template.** It is retained only at the user's request.
  It also demonstrates exactly why §5 exists: generating it embedded a real GitHub token
  into a file that was then committed to a public repo.
- **`CLAUDE_PROMPT.md`** — formerly a near-duplicate of this file describing the same dead
  workflow. Now a pointer here.
- **`HANDOVER.md` §2 and §8** — §2's "two conflicting workflows, unresolved" question is
  settled by this file. §8's conclusion that prompt-only formatting fails was reached on a
  model that no longer exists; treat it as unverified.

---

## 8. Architecture notes that have already caused bugs

Fuller detail in `HANDOVER.md` §9 and §12.

- **`_ = _overlay.Handle;` in the `TrayApp` constructor is load-bearing.** It forces handle
  creation on the UI thread. Without it `InvokeRequired` returns `false` from background
  threads before the first `Show()`, and the overlay silently never appears.
- **`RegisterHotKey`/`UnregisterHotKey` are thread-affine** to the owning window's message
  queue. Calling either from a `Task.Run` continuation fails *silently* and leaks the
  global registration — Escape then stays captured system-wide. Marshal via
  `_overlay.BeginInvoke`. `ToggleDictation()` must be called on the UI thread for this
  reason.
- **The overlay is `WS_EX_NOACTIVATE`** so it never steals focus — essential, because the
  result is pasted with `Ctrl+V` into whatever the user was typing in. Consequences:
  `BringToFront()` is inert, and WinForms ignores re-setting `TopMost = true`. Z-order is
  re-asserted via `SetWindowPos` on a timer. Child `Label`/`Panel` clicks do **not** bubble
  to the `Form`, so each surface is wired individually.
- **Groq retires models.** A dead chat model returns `404` and, because of the graceful
  degradation above, looks like a sound bug rather than an outage. Check the log for
  `POLISH-FAILED`, then `GET https://api.groq.com/openai/v1/models`.
- **Whisper hallucinates on near-silence**, usually `"ご視聴ありがとうございました"`. Audio
  is gated on `AudioRecorder.SpeechDetected`; a length check cannot catch it, because a
  long silent recording passes easily.
- **Tray icons are drawn, not extracted.** Filled shapes survive the drop to 16px;
  antialiasing turns a 1px stroke into two half-lit pixels, so outlined designs go grey
  rather than merely small.

---

## 9. Testing

There are no automated tests. Verification is manual and empirical:

- Pure functions (the text formatter) are checked by generating a throwaway console app in
  the scratchpad that includes the real methods from source and runs them over real failing
  examples. Do this rather than reasoning about regexes — it has caught real bugs.
- Anything touching the OS (hotkeys, z-order, audio) needs a live probe or the trace log.
- Sound and icon changes need the user's eyes and ears. Render icon options and show them
  before committing to one.
