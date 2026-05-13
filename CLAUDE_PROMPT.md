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
2. Claude edits the source files directly in the project
3. Claude generates a `deploy.py` for me to download and double-click
4. I test it — if happy I type Y to install, then Y again to push to GitHub
5. **GitHub is only pushed after I confirm everything works**

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

### Deploy script template

The following is the full deploy.py template. When generating a new deploy.py, take this template and substitute `<<CS_B64>>`, `<<CSPROJ_B64>>`, `<<README_B64>>` with the actual base64-encoded content of the current source files, and `<<GITHUB_TOKEN>>` with the token from `_claude_config.txt`:

```python
import base64, os, shutil, subprocess, sys, time, stat
import urllib.request, urllib.error, json, traceback

INSTALL_DIR  = r"C:\Users\grant.rogers\My Apps\VoiceDictation"
WORK_DIR     = os.path.join(os.environ["TEMP"], "VoiceDictation-build")
DIST_DIR     = os.path.join(WORK_DIR, "dist")
GITHUB_TOKEN = "<<GITHUB_TOKEN>>"
GITHUB_OWNER = "granterogers"
GITHUB_REPO  = "voice-dictation"

CS_B64     = "<<CS_B64>>"
CSPROJ_B64 = "<<CSPROJ_B64>>"
README_B64 = "<<README_B64>>"

def step(n, total, msg): print(f"\n[{n}/{total}] {msg}")
def ok(msg):             print(f"      {msg}")

def err(msg):
    print(f"\nERROR: {msg}")
    input("\nPress Enter to exit...")
    sys.exit(1)

def force_remove(path):
    def on_error(func, fpath, _):
        try:
            os.chmod(fpath, stat.S_IWRITE)
            func(fpath)
        except Exception as e:
            print(f"  Warning: could not remove {fpath}: {e}")
    shutil.rmtree(path, onerror=on_error)

def find_dotnet():
    import shutil as sh
    found = sh.which("dotnet")
    if found:
        return found
    for c in [
        r"C:\Program Files\dotnet\dotnet.exe",
        r"C:\Program Files (x86)\dotnet\dotnet.exe",
        os.path.join(os.environ.get("ProgramFiles", ""), "dotnet", "dotnet.exe"),
        os.path.join(os.environ.get("LOCALAPPDATA", ""), "Microsoft", "dotnet", "dotnet.exe"),
    ]:
        if os.path.exists(c):
            return c
    return None

def kill_app():
    subprocess.run(["taskkill", "/IM", "VoiceDictation.exe", "/F"], capture_output=True)

def is_running():
    r = subprocess.run(["tasklist", "/FI", "IMAGENAME eq VoiceDictation.exe", "/NH"],
                       capture_output=True, text=True)
    return "VoiceDictation.exe" in r.stdout

def wait_exit(timeout=8):
    deadline = time.time() + timeout
    while time.time() < deadline:
        if not is_running():
            return True
        time.sleep(0.5)
    return False

def launch_and_verify(exe, timeout=5):
    proc = subprocess.Popen([exe])
    time.sleep(timeout)
    if proc.poll() is not None:
        return False, f"Process exited immediately with code {proc.returncode}"
    if not is_running():
        return False, "Process not found in tasklist after launch"
    return True, "OK"

def copy_retry(src, dst, retries=5, delay=1):
    for i in range(retries):
        try:
            shutil.copy2(src, dst)
            return
        except PermissionError:
            if i < retries - 1:
                print(f"  File locked, retrying ({i+1}/{retries})...")
                time.sleep(delay)
            else:
                raise

def github_push(files_b64):
    headers = {
        "Authorization": f"token {GITHUB_TOKEN}",
        "Accept": "application/vnd.github.v3+json",
        "Content-Type": "application/json",
    }
    for filename, content_b64 in files_b64.items():
        url = f"https://api.github.com/repos/{GITHUB_OWNER}/{GITHUB_REPO}/contents/{filename}"
        sha = None
        try:
            req = urllib.request.Request(url, headers=headers)
            with urllib.request.urlopen(req) as resp:
                sha = json.loads(resp.read()).get("sha")
        except urllib.error.HTTPError as e:
            if e.code != 404:
                raise
        body = {"message": f"Update {filename}", "content": content_b64}
        if sha:
            body["sha"] = sha
        req = urllib.request.Request(url, method="PUT",
            data=json.dumps(body).encode(), headers=headers)
        try:
            urllib.request.urlopen(req)
            print(f"    Pushed {filename}")
        except urllib.error.HTTPError as e:
            raise RuntimeError(f"GitHub API error {e.code} for {filename}: {e.read().decode()}") from e

try:
    TOTAL = 4
    print()
    print("============================================")
    print("  VoiceDictation Updater")
    print(r"  Win+\ again = stop & process now")
    print("  Escape = cancel dictation")
    print("============================================")

    step(1, TOTAL, "Writing latest source files...")
    if os.path.exists(WORK_DIR):
        force_remove(WORK_DIR)
    os.makedirs(WORK_DIR)
    os.makedirs(DIST_DIR)

    try:
        cs_bytes     = base64.b64decode(CS_B64)
        csproj_bytes = base64.b64decode(CSPROJ_B64)
    except Exception as e:
        err(f"Embedded source is corrupt: {e}")

    with open(os.path.join(WORK_DIR, "Program.cs"), "wb") as f:
        f.write(cs_bytes)
    with open(os.path.join(WORK_DIR, "voice-dictation.csproj"), "wb") as f:
        f.write(csproj_bytes)
    ok(f"Source files written to {WORK_DIR}")

    step(2, TOTAL, "Building...")
    dotnet = find_dotnet()
    if not dotnet:
        err("dotnet.exe not found. Install .NET 8 SDK: https://dotnet.microsoft.com/en-us/download/dotnet/8.0")
    ok(f"Using dotnet: {dotnet}")

    result = subprocess.run([
        dotnet, "publish", "-c", "Release", "-r", "win-x64",
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:EnableWindowsTargeting=true",
        "-o", DIST_DIR
    ], cwd=WORK_DIR)
    if result.returncode != 0:
        err("Build failed — see output above.")

    dist_exe = os.path.join(DIST_DIR, "VoiceDictation.exe")
    if not os.path.exists(dist_exe):
        err(f"Build output not found at {dist_exe}")
    ok("Build succeeded.")

    step(3, TOTAL, "Launching new version for testing...")

    for fname in ["groq_key.txt", "settings.json"]:
        src = os.path.join(INSTALL_DIR, fname)
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(DIST_DIR, fname))
        elif fname == "groq_key.txt":
            err(f"groq_key.txt not found at {src} — test version won't start without it.")

    if is_running():
        print("  Stopping current instance to allow test version to run...")
        kill_app()
        if not wait_exit(timeout=6):
            err("Could not stop the running instance. Please close it manually and retry.")
        ok("Current instance stopped.")

    print("  Starting test version...")
    running, msg = launch_and_verify(dist_exe, timeout=4)
    if not running:
        err(f"Test version failed to start: {msg}")
    ok("Test version is running.")

    print()
    print(r"  Win+\ to start, Win+\ again to stop & process, Escape to cancel.")
    print()
    happy = input("  Happy with it? Type Y to install, N to abort: ").strip().lower()

    kill_app()
    wait_exit(timeout=5)

    if happy != "y":
        print("\nAborted. Restarting your original installed version...")
        installed_exe = os.path.join(INSTALL_DIR, "VoiceDictation.exe")
        if os.path.exists(installed_exe):
            subprocess.Popen([installed_exe])
        input("\nPress Enter to exit...")
        sys.exit(0)

    step(4, TOTAL, "Installing...")

    installed_exe = os.path.join(INSTALL_DIR, "VoiceDictation.exe")
    print("  Backing up old version...")
    if os.path.exists(installed_exe):
        shutil.copy2(installed_exe, installed_exe + ".bak")

    print("  Copying new version...")
    try:
        copy_retry(dist_exe, installed_exe)
    except Exception as e:
        err(f"Failed to copy exe after retries: {e}")

    print("  Starting updated VoiceDictation...")
    subprocess.Popen([installed_exe])
    ok("VoiceDictation updated and restarted!")

    print()
    push = input("Push source to GitHub? (Y/N): ").strip().lower()
    if push == "y":
        print("  Pushing to GitHub...")
        try:
            github_push({
                "voice-dictation-program.cs": CS_B64,
                "voice-dictation.csproj":     CSPROJ_B64,
                "README.md":                  README_B64,
            })
            ok("Pushed to GitHub!")
        except Exception as e:
            print(f"  Warning: GitHub push failed: {e}")

    print()
    input("Press Enter to exit...")

except KeyboardInterrupt:
    print("\nCancelled.")
    input("Press Enter to exit...")
    sys.exit(0)
except Exception as e:
    print(f"\nUnexpected error: {e}")
    traceback.print_exc()
    input("\nPress Enter to exit...")
    sys.exit(1)
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
- Default system prompt baked into source:
  > You are a dictation cleanup assistant. Fix punctuation, capitalization, and sentence structure. Keep the user's tone and meaning exactly. Do NOT add, remove, or change any content beyond fixing grammar. Add blank lines where context implies paragraph breaks (e.g. between email greeting, body, and sign-off). Output ONLY the cleaned text. Do NOT write anything before or after it. Do NOT explain what you changed.

### Audio
- NAudio 2.2.1
- Silence detection: 2s accumulation after speech starts, ignored for first 2s
- Max recording: 120s
- Live transcription preview every ~2s via Groq Whisper
- `StopNow()` public method on `AudioRecorder` for hotkey-triggered stop

### Settings
- Saved to `settings.json` next to the exe
- Loaded from `AppContext.BaseDirectory` with fallback to `Directory.GetCurrentDirectory()`
- Includes: input device, sounds, overlay appearance, AI prompt

---

## Known Past Mistakes to Avoid

- **Do not add NuGet.config or global.json** to the build folder — causes version mismatch errors between SDK and local cache
- **Do not use an empty assistant prefill** in the Polish API call — it prevents the model from reasoning about formatting (e.g. email paragraph breaks)
- **Do not restrict NuGet to local cache** — the local cache may be one patch version behind the SDK
- **Always kill the running instance before launching the test version** — mutex causes silent exit otherwise
- **Always use `force_remove()` not `shutil.rmtree()`** — the build folder may contain read-only `.git` files from a previous run that `rmtree` can't delete on Windows
