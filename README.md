> **AI assistants:** Read `AGENTS.md` in full before making any changes to
> this project. It contains workflow rules, deploy script requirements, and
> presentation preferences that must be followed.

# Voice Dictation

Windows tray app that records your voice, transcribes via Groq Whisper, cleans up with LLaMA, and pastes the result at your cursor.

Press **Win+\** to start recording. Speak naturally — the app automatically detects when you stop talking.

## Hotkeys

| Hotkey | Action |
|--------|--------|
| **Win+\** | Start recording |
| **Win+\** *(while recording)* | Stop and process immediately |
| **Escape** *(while recording)* | Cancel — nothing is pasted |

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
| `/openai/v1/chat/completions` | `llama-3.3-70b-versatile` | Text cleanup |
