> **AI assistants:** Read `AGENTS.md` in full before making any changes to this
> project. It is the single canonical instruction set — workflow, build commands,
> secret-handling rules, and the architecture traps that have already caused bugs.
> `HANDOVER.md` has the engineering history behind it.

# Voice Dictation

Windows tray app that records your voice, transcribes via Groq Whisper, cleans up with LLaMA, and pastes the result at your cursor.

Press **Win+\** to start recording. Speak naturally — the app automatically detects when you stop talking.

## Hotkeys

| Hotkey | Action |
|--------|--------|
| **Win+\** | Start recording |
| **Win+\** *(while recording)* | Stop and process immediately |
| **Escape** *(while recording)* | Cancel — nothing is pasted |

## Mouse

| Action | Result |
|--------|--------|
| **Left-click the tray icon** | Start dictation |
| **Left-click again** | Finished speaking — clean up and paste |
| **Click the overlay** | Finished speaking — clean up and paste |
| **Click the overlay's ✕** | Cancel — nothing is pasted |
| **Right-click the tray icon** | Settings / version / quit |

The tray icon is blue when idle and red while recording.

Click-started dictation keeps listening until you stop it — it does **not** auto-stop on
silence, so you can pause mid-thought. (`Win+\` still auto-stops after ~2s of silence.)
A click-started cycle in which you never speak gives up after 15 seconds.

## Requirements

- Windows 10/11 (x64)
- .NET 8 SDK — `winget install Microsoft.DotNet.SDK.8`
- Groq API key in `groq_key.txt` — sign up free at [console.groq.com](https://console.groq.com)

## Setup

Place `voice-dictation.csproj`, `voice-dictation-program.cs`, and `groq_key.txt` in a folder, then run `dotnet run`.

## How It Works

1. **Win+\** → start sound plays, overlay appears
2. Live transcript preview updates every ~1 second while you speak
3. Recording stops after ~2 seconds of silence (or press **Win+\** to stop immediately)
4. Full audio transcribed via **Groq Whisper**
5. Text cleaned up via **Groq LLaMA**
6. Result pasted at cursor via Ctrl+V

## API Details

| Endpoint | Model | Purpose |
|----------|-------|---------|
| `/openai/v1/audio/transcriptions` | `whisper-large-v3-turbo` | Speech-to-text |
| `/openai/v1/chat/completions` | `openai/gpt-oss-120b` | Text cleanup |

> Groq retires models periodically. `llama-3.3-70b-versatile` was previously used here
> and now returns `404`. Because the cleanup step degrades gracefully to raw transcript
> text, a dead model shows up only as an error sound and unpolished output — check
> `voice-dictation.log` for `POLISH-FAILED`, then
> `GET https://api.groq.com/openai/v1/models` for what is currently available.

## Sounds

Settings has a **Scheme** dropdown (Minimal / Soft / Crisp / Window / Classic) with a
Preview button. Sound names resolve against a `sounds\` folder next to the exe first,
then `C:\Windows\Media`; absolute paths also work. Both `.wav` and `.mp3` are supported.

`sounds\` is gitignored and absent from a fresh clone, so any scheme referencing it
falls back to the all-Windows "Soft" set automatically.

## Troubleshooting

`voice-dictation.log`, written next to the exe, records each dictation cycle: every
sound and the line that triggered it, and `RAW` → `POLISHED` → `FORMATTED` text. It
never records your API key. Start here before debugging anything.
