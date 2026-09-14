# HANDOVER — VoiceDictation

Prepared 2026-09-14 for handoff to another code-generation session/tool.
Read this in full before making changes — several things here are non-obvious and have already
caused real bugs.

---

## 1. What this is

A Windows system-tray app, C# / .NET 8 / Windows Forms, single file `voice-dictation-program.cs`
(~950 lines) + `voice-dictation.csproj`. Press **Win+\\** to record, speak, it auto-stops on
silence, transcribes via Groq Whisper, cleans up the text via Groq LLaMA, and pastes the result at
the cursor via Ctrl+V.

- GitHub: `github.com/granterogers/voice-dictation` (owner: `granterogers`)
- Local working copy: `C:\Users\grant.rogers\My Apps\DEV\voice-dictation`
- Runtime API key: `groq_key.txt`, plain text, one line, sits next to the built exe (see §6)

## 2. ⚠️ Two conflicting workflows exist in this repo — resolve before continuing

The repo contains **two different, incompatible process documents**, and recent work has not
followed either consistently. Whoever picks this up next should reconcile them; don't just pick
one silently.

**Workflow A — documented in `AGENTS.md` / `CLAUDE_PROMPT.md` (older, presented as canonical):**
- Claude edits source directly, then generates a `deploy.py` (source embedded as base64) for the
  user to double-click.
- The script builds, launches the test build, asks "Happy with it? Y/N", and only on Y does it
  install over the live copy and optionally push to GitHub via the **Contents API** (no git).
- Explicitly states: *"Do NOT restrict NuGet to local cache"*, *"Do NOT write a NuGet.config or
  global.json"*, *"GitHub is storage only — never pull from GitHub at the start of a session"*.
- A generated `deploy.py` (57K tokens, base64 blobs) is currently committed at the repo root —
  it's a stale snapshot from a previous run, not a template to maintain by hand.

**Workflow B — what actually happened this session:**
- Direct `git commit` + `git push origin main` on every change.
- Versioning by a **global PostToolUse hook** (`~/.claude/hooks/version-on-push.ps1`, wired in
  `~/.claude/settings.json`, not project-specific) that tags `v1.0.0` on the first push to a repo
  and bumps the **minor** version on every push after (`v1.1.0`, `v1.2.0`, …). This is documented
  in the user's global `~/.claude/CLAUDE.md`, not in this repo.
- A `NuGet.config` **is committed to this repo** (root) that does the exact thing Workflow A warns
  against:
  ```xml
  <packageSources>
    <clear />
    <add key="local-cache" value="%USERPROFILE%\.nuget\packages" />
  </packageSources>
  ```
  This made the very first `dotnet publish` of this session fail (`NU1102`, needed runtime pack
  `8.0.30`, local cache only had up to `8.0.28`). It was worked around per-command with
  `--source "https://api.nuget.org/v3/index.json" --source "<nuget cache path>"` flags rather than
  fixed at the source. **Every build command in this session's history uses that workaround** — if
  you build without it, expect it to fail again, exactly as `AGENTS.md` predicted.

**Open decision for the user:** either (a) delete the committed `NuGet.config` and
`deploy.py`, update `AGENTS.md`/`CLAUDE_PROMPT.md` to describe the git+hook workflow that's
actually in use now, or (b) revert to the deploy.py/Contents-API workflow and stop pushing via
raw git. Don't silently do either — ask.

## 3. Build & run (current working incantation)

```powershell
# Kill any running instance first (single-instance mutex; also locks the exe file for publish)
Get-Process -Name VoiceDictation -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

dotnet publish "voice-dictation.csproj" -c Release -r win-x64 `
  --source "https://api.nuget.org/v3/index.json" `
  --source "C:\Users\grant.rogers\.nuget\packages"

# groq_key.txt must already exist next to the exe (see §6), or the app opens a
# file-picker on first run and copies whatever you pick into place.
Start-Process "bin\Release\net8.0-windows\win-x64\publish\VoiceDictation.exe"
```

Output: `bin\Release\net8.0-windows\win-x64\publish\VoiceDictation.exe` — self-contained,
single-file, ~155 MB (bundles the whole .NET runtime). Build ends with a
`Build version: vX.Y.Z` line (see §7).

`bin/` and `obj/` are untracked/ignored from commits — don't add them.

## 4. Source map

| File | Purpose |
|---|---|
| `voice-dictation-program.cs` | Entire application. See §5 for the class breakdown. |
| `voice-dictation.csproj` | Project file + the git-tag-to-version MSBuild targets (see §7) |
| `NuGet.config` | **Problematic** — see §2 |
| `README.md` | User-facing docs (hotkeys, setup, API details) |
| `AGENTS.md`, `CLAUDE_PROMPT.md` | Old process docs — see §2. Near-duplicates of each other. |
| `deploy.py` | Stale generated artifact from the old workflow, huge (base64 blobs). Not hand-maintained. |

## 5. Code architecture (`voice-dictation-program.cs`)

- `Program` — entry point, single-instance `Mutex`, `Application.Run(new TrayApp())`.
- `AppVersion` — reads `AssemblyInformationalVersionAttribute` (set at build time from the git tag,
  see §7); `AppVersion.Current` is a `"vX.Y.Z"` string used in the Settings window title, a label
  near its Save/Cancel buttons, and the tray context menu.
- `AppSettings` — POCO persisted to `settings.json` next to the exe (`AppContext.BaseDirectory`,
  falls back to `Directory.GetCurrentDirectory()`). Holds sound file choices, `SoundsEnabled` bool,
  overlay colors/opacity/font size, input device index, and `PolishPrompt` (the AI cleanup system
  prompt, user-editable in Settings, defaults to a hardcoded email-formatting instruction).
- `SettingsForm` — the Settings dialog (input device, sounds, overlay preview/colors, AI prompt
  textbox, version label).
- `OverlayForm` — always-on-top borderless status/preview window shown during dictation. Its handle
  **must** be forced into existence in the `TrayApp` constructor (`_ = _overlay.Handle;`) — without
  this, `InvokeRequired` reports `false` from background threads before the first `Show()`, and the
  overlay silently never appears. (Documented in `AGENTS.md`, still true.)
- `TrayApp : ApplicationContext` — the whole runtime: tray icon, both global hotkeys, the
  record → transcribe → polish → paste pipeline (`RunPipeline`), `Polish()` (Groq LLaMA call),
  `Transcribe()` (Groq Whisper call), `EnforceMessageStructure()` (deterministic paragraph
  formatter, see §8), `LoadApiKey()` (see §6).
- `HotkeyWindow : NativeWindow` — thin wrapper that owns the hidden window used for
  `RegisterHotKey`/`UnregisterHotKey` and dispatches `WM_HOTKEY` (0x0312) to a callback.
  **Thread-affinity gotcha, already bit us once (see §9.1): both register and unregister must run
  on the thread that created this window (the UI thread).** Never call either from a `Task.Run`
  continuation without marshaling back — `_overlay.BeginInvoke(...)` is the pattern used elsewhere
  in this file.
- `AudioRecorder` (NAudio-backed) — mic capture, 2s silence-detection auto-stop, 120s hard cap,
  live-preview WAV snapshotting for the in-progress transcript preview.

## 6. The Groq API key

- Looked for at `AppContext.BaseDirectory\groq_key.txt` (i.e. next to the exe — for this
  self-contained single-file publish, that's the `publish\` folder) and
  `Directory.GetCurrentDirectory()\groq_key.txt`.
- **User confirmed this session:** assume it's already sitting in the publish folder next to the
  exe; don't ask or warn about it unless that folder was wiped or this is a fresh clone.
- If missing, `LoadApiKey()` opens an `OpenFileDialog` (browse for it) instead of a dead-end error,
  and copies whatever's picked into `AppContext.BaseDirectory\groq_key.txt` for next time.
- It is a live credential — never create, print, or commit its contents.

## 7. Version numbering — how it's wired end to end

1. On every `git push`, the user's global hook (outside this repo) creates/bumps a `vX.Y.Z` tag —
   first push ever creates `v1.0.0`, every push after bumps **minor** (patch always `0`).
2. `voice-dictation.csproj` has a `SetVersionFromGitTag` MSBuild target
   (`BeforeTargets="GetAssemblyVersion;GenerateAssemblyInfo"`) that shells out to
   `git tag --points-at HEAD --sort=-v:refname` (falls back to `git describe --tags --abbrev=0` if
   nothing points exactly at HEAD), takes the first result, and sets it as
   `$(InformationalVersion)`. `IncludeSourceRevisionInInformationalVersion` is set `false` so the
   SDK doesn't append a `+<commit-sha>` suffix.
3. A `PrintVersionAfterPublish` target (`AfterTargets="Publish"`) prints
   `Build version: vX.Y.Z` as (intentionally) the last line of `dotnet publish` output.
4. At runtime, `AppVersion.Current` reads that back via
   `Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()`.

**Consequence — version is only ever as fresh as the last build.** The tag is created by the push
*hook*, which fires *after* `git push` returns, so a build done before pushing bakes in the
*previous* tag. **Standing rule for this project: always rebuild (and relaunch) immediately after
every push**, so the running exe's displayed version never lags the GitHub tag. This is saved as
persistent memory for this project; don't reintroduce the lag.

**Known quirk:** the push hook has, on at least two occasions this session, fired twice for what
looked like a single logical push (once via a `Bash`-tool `git push` that returned exit 128 with no
output, once via a `PowerShell`-tool retry that actually succeeded) — resulting in two tags
(`v1.0.0`+`v1.1.0`, then later `v1.3.0`+`v1.4.0`) landing on the same commit. Harmless (both point
at the same commit, "latest" is still correct), but don't be alarmed if `git tag` shows a tag you
didn't expect — verify with `git log` before assuming something is broken. **Prefer running `git
push` via the `PowerShell` tool, not `Bash`** — the Bash-tool git push in this environment
returned a silent exit 128 every time it was tried, for reasons not diagnosed.

## 8. AI cleanup formatting — why there's a code-level formatter, not just a prompt

The Groq LLaMA cleanup step (`Polish()`) is prompted to format dictated text like an email
(greeting alone on its own line, sign-off alone on its own line, body broken into short
paragraphs). **This prompt-only approach failed reliably across three real, distinct user
examples** (short message with greeting+sign-off, long multi-step technical explanation, another
short message) — the model kept emitting one dense block regardless of prompt wording, examples,
or lowering `temperature` (currently `0.15`).

The fix that actually worked: `EnforceMessageStructure()`, a **deterministic, regex-based
post-processor** run on the polished text before it's copied to the clipboard. It:
- Detects a leading `Hi|Hey|Hello|Dear|Greetings <Name>` and splits it onto its own line.
- Detects a trailing sign-off from a fixed phrase list (`"best wishes"`, `"thanks"`, `"regards"`,
  etc.) and splits it onto its own line.
- Splits the remaining body into sentences and groups them into paragraphs of at most 2–3
  sentences, forcing a new paragraph at transition words (`but`, `so`, `however`, `then`, `also`,
  `thereafter`, `additionally`, `meanwhile`).
- Leaves text with no greeting/sign-off (or genuinely short one-liners) untouched.

This was verified with a standalone throwaway console app (not committed) run against the three
real failing examples before being wired into the pipeline — all three came out correctly
structured. **If you touch this again, don't revert to prompt-only tuning without re-running that
kind of test** — it's a known dead end for this model/temperature combination.

## 9. Bugs found & fixed this session (read before touching hotkeys)

### 9.1 Escape hotkey leaking into every other application (root-caused, fixed)

Symptom: pressing Escape did nothing in *other* apps, seemingly at random, even when
VoiceDictation wasn't actively recording.

Two fixes were needed, in this order:

1. **First pass (incomplete):** Escape was originally registered as a global OS hotkey for the
   entire app lifetime (constructor). Changed so it's registered only when a dictation cycle
   starts (`_isRecording = true`) and unregistered in the pipeline's `finally`. This *looked*
   right but didn't fully fix the bug.
2. **Real root cause:** `RegisterHotKey`/`UnregisterHotKey` are **thread-affine to the window's
   message queue**. The register call ran correctly on the UI thread (triggered from `WndProc`),
   but the unregister call was inside the `Task.Run(async () => ...)` background thread's
   `finally` block — a *different* thread. That call **silently failed** (its `bool` return value
   was never checked) — Windows never actually released the registration, so Escape stayed
   globally captured forever after the very first dictation, even with the app sitting idle.

   Fixed by marshaling the unregister call onto the UI thread:
   ```csharp
   try { _overlay.BeginInvoke(new Action(() =>
       HotkeyWindow.UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_ESCAPE))); }
   catch { }
   ```

**How this was actually verified** (don't skip this kind of check if you touch hotkey code — the
bug was invisible from reading the code alone until proven with a live probe):
```powershell
# Probe: try to claim bare VK_ESCAPE (0x1B) as a global hotkey from a throwaway process.
# Success (True) = nobody holds it. Failure with LastError 1409 = something already holds it.
Add-Type -Name Win32 -Namespace Test -MemberDefinition '
[DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
[DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);'
[Test.Win32]::RegisterHotKey([IntPtr]::Zero, 999, 0, 0x1B)  # $false + LastError 1409 = stuck
```
Confirmed the culprit by killing the VoiceDictation process and re-running the probe (it freed up
immediately), then confirmed the fix by simulating a real `Win+\` keypress (`keybd_event`), letting
a full recording cycle complete, and re-running the probe **while the app was still running** — it
succeeded, meaning the OS-level registration was correctly released.

### 9.2 Escape scope, as the user actually wants it

Current (correct, as of this handover) behavior: Escape does **nothing at all** unless a dictation
cycle is in progress — from the moment `Win+\` starts recording, through transcription and AI
cleanup, until the result is pasted (or the cycle errors/cancels out). Outside that window, Escape
is not registered at all and behaves completely normally in every other application. If a future
change narrows or widens this window, re-verify with the probe technique above, not just by
reasoning about the code — the bug in 9.1 was invisible without it.

## 10. Open work for this handoff

1. **Volume control for sounds** — requested, not yet implemented as of this document's first
   version. `PlaySound()` currently uses `System.Media.SoundPlayer`, which has no volume API.
   Implementing real volume control requires switching playback to NAudio (already a project
   dependency) — e.g. `AudioFileReader` (has a `.Volume` float 0.0–1.0) through `WaveOutEvent`,
   plus a new `SoundVolume` field on `AppSettings` and a slider in `SettingsForm` next to the
   existing "Enable sounds" checkbox.
2. **GitHub issues** — the user wants a backlog tracked in GitHub Issues. As of this document,
   the repo has zero open issues. Candidates worth filing (confirm with the user before creating
   more than what's already agreed): the volume control feature above; the two-conflicting-
   workflows situation in §2; the three pre-existing build warnings (`CS8632` nullable annotation
   context, `CS0219` unused `hasLivePreview`, `CS0414` unused `TrayApp._stopRequested`); a
   user-configurable hotkey (currently hardcoded to `Win+\`).
3. Not yet addressed: no automated tests exist anywhere in this repo. All verification this
   session was manual/empirical (see §8 and §9.1 for the two cases where that mattered most).

## 11. Persistent memory already recorded for this project

A separate Claude memory store (outside this repo, at
`~/.claude/projects/<project-hash>/memory/`) already holds three standing rules for this project
that a fresh session/tool won't see unless told:
- Kill any running `VoiceDictation` process before rebuilding, and relaunch the fresh exe after —
  no need to ask each time.
- `groq_key.txt` lives in the publish folder next to the exe by default — don't ask about it.
- Always rebuild + relaunch immediately after every push, so the running app's version never lags
  the GitHub tag (see §7).

If picking this up in a tool without access to that memory store, treat the three bullets above as
still-active standing instructions from the user.
