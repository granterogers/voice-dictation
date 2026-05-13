# AGENTS.md — VoiceDictation

> This file is the canonical instruction set for AI assistants working on this project.
> Read it in full before making any changes, generating any files, or responding to requests.

---

# Claude Project Prompt — VoiceDictation

Paste this into a new Claude conversation (along with the three project source files attached) to resume working on this project seamlessly.

---

## Project Overview

I'm working on **VoiceDictation** — a C# / .NET 8 / Windows Forms system tray app that records voice via Win+\, transcribes via Groq Whisper, cleans up with Groq LLaMA, and pastes the result at the cursor.

- **GitHub repo:** `github.com/granterogers/voice-dictation`
- **Install path:** `C:\Users\grant.rogers\My Apps\VoiceDictation`
- **Stack:** C# / .NET 8 / Windows Forms / NAudio / Groq API (Whisper + LLaMA)
- **GitHub token:** stored in `_claude_config.txt` uploaded each session

---

## Source of Truth

**Claude is the source of truth for all code.** Changes originate here and flow outward. The project files attached to this conversation are canonical. GitHub is storage only — never pull from GitHub at the start of a session.

The three source files are:

- `voice-dictation-program.cs` — full application source
- `voice-dictation.csproj` — project file
- `README.md` — documentation

---

## How to Make Changes

1. I describe a change
2. Claude reads AGENTS.md in full before doing anything
3. Claude edits the source files directly in the project
4. Claude generates a `deploy.py` for me to download and double-click
5. I test it — if happy I type Y to install, then Y again to push to GitHub
6. **GitHub is only pushed after I confirm everything works**

---

## Deploy Script

When generating `deploy.py`, Claude must follow these rules exactly:

### Critical rules

- Embed all source files as **base64 strings** inside the script — no git pull needed
- **Do NOT write a NuGet.config or global.json** — these caused version mismatch errors in the past. Let dotnet use its default config and reach the internet normally.
- Clean the temp build folder at startup using `force_remove()` with `stat.S_IWRITE` stripping — handles read-only `.git` files from previous runs
- Find `dotnet.exe` by checking PATH first, then common install locations — it may not be on PATH when double-clicked
- **Kill any running instance before launching the test version** — the app has a single-instance mutex and will silently exit if another copy is already running
- After launching, verify the process actually stayed alive for a few seconds before asking if the user is happy
- On Y: kill test instance, back up old exe, copy new exe with retries (handles brief post-kill file lock), restart app
- On N: kill test instance, restart original installed version so user is never left without the app
- GitHub push via Contents API (no git): GET each file for its SHA, then PUT with base64 content
- Python stdlib only — no pip installs
- Every exit point must end with `input("Press Enter to exit...")` so the console stays open
- Push these three files to GitHub: `voice-dictation-program.cs`, `voice-dictation.csproj`, `README.md`

### Configuration

```
INSTALL_DIR  = r"C:\Users\grant.rogers\My Apps\VoiceDictation"
WORK_DIR     = os.path.join(os.environ["TEMP"], "VoiceDictation-build")
GITHUB_OWNER = "granterogers"
GITHUB_REPO  = "voice-dictation"
EXE_NAME     = "VoiceDictation.exe"
```

---

## Key App Architecture

### Hotkeys

- **Win+\** — start recording
- **Win+\ again while recording** — stop immediately and process (plays done sound)
- **Escape while recording** — cancel silently, hide overlay, nothing pasted
- Both hotkeys registered via `HotkeyWindow` which passes the hotkey ID to the callback

### Overlay (always-on-top)

- Uses `SetWindowPos` with `HWND_TOPMOST` + `Update()` called on every show/update
- **Critical:** `_ = _overlay.Handle;` must be called in `TrayApp` constructor to force handle creation on the UI thread. Without this, `InvokeRequired` returns false on background threads before first show, causing the overlay to never appear.

### AI Cleanup (Polish)

- Model: `llama-3.3-70b-versatile`
- System prompt is appended with a hard REMINDER before sending to reinforce output-only behaviour
- Post-processing strips trailing commentary lines starting with "Note:", "I have", "I've ", "Changes made:", etc.

### Audio

- NAudio 2.2.1
- Silence detection: 2s accumulation after speech starts, ignored for first 2s
- Max recording: 120s
- Live transcription preview fires concurrently every ~1s of new audio via Groq Whisper
- `StopNow()` public method on `AudioRecorder` for hotkey-triggered stop

### Settings

- Saved to `settings.json` next to the exe
- Loaded from `AppContext.BaseDirectory` with fallback to `Directory.GetCurrentDirectory()`
- Includes: input device, sounds, overlay appearance, AI prompt

---

## Known Past Mistakes to Avoid

- **Do not add NuGet.config or global.json** to the build folder — causes version mismatch errors between SDK and local cache
- **Do not use an empty assistant prefill** in the Polish API call — it prevents the model from reasoning about formatting
- **Do not restrict NuGet to local cache** — the local cache may be one patch version behind the SDK
- **Always kill the running instance before launching the test version** — mutex causes silent exit otherwise
- **Always use `force_remove()` not `shutil.rmtree()`** — the build folder may contain read-only `.git` files from a previous run that `rmtree` can't delete on Windows
