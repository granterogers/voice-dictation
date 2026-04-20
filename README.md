# Voice Dictation

A lightweight Windows system tray application that records your voice, transcribes it in real time, cleans up the text using AI, and pastes the result at your cursor position.

Press **Win+\\** to start recording. Speak naturally — the app automatically detects when you stop talking and handles the rest.

## Hotkeys

| Hotkey | Action |
|--------|--------|
| **Win+\\** | Start recording |
| **Win+\\** *(while recording)* | Stop recording immediately and process |
| **Escape** *(while recording)* | Cancel recording — nothing is pasted |

## How It Works

1. **Win+\\** → a start sound plays and a small overlay window appears near the system tray
2. As you speak, the overlay shows a live preview of your transcription (updated every ~2 seconds)
3. Recording stops automatically after ~2 seconds of silence — or press **Win+\\** again to stop immediately
4. The full audio is transcribed via **Groq Whisper** (speech-to-text)
5. The raw transcript is sent to **Groq LLaMA** for grammar/punctuation cleanup
6. The cleaned text is copied to your clipboard and pasted at the current cursor position
7. A completion sound plays and the overlay disappears

Press **Escape** at any point during recording to cancel without pasting anything.

## Requirements

- **Windows 10/11** (x64)
- **.NET 8 SDK** — install via `winget install Microsoft.DotNet.SDK.8` or from [dot.net](https://dot.net)
- **Groq API key** — sign up free at [console.groq.com](https://console.groq.com) and generate an API key

## Setup

### 1. Get the code

Place these files in a folder (e.g. `C:\VoiceDictation\`):

```
VoiceDictation/
├── voice-dictation.csproj
├── voice-dictation-program.cs
├── groq_key.txt
└── README.md
```

### 2. Add your API key

Create a file called `groq_key.txt` in the same folder. Paste your Groq API key as the only content:

```
gsk_your_actual_key_here
```

No quotes, no extra lines — just the key.

### 3. Run in development mode

```
cd C:\VoiceDictation
dotnet run
```

The app will appear in your system tray. The first run may take a moment while NuGet restores packages.

### 4. Build a standalone executable (optional)

```
dotnet publish -c Release
```

The compiled binary will be at:

```
bin\Release\net8.0-windows\win-x64\publish\VoiceDictation.exe
```

Copy `groq_key.txt` into the same folder as the `.exe` before running it.

## Settings

Right-click the tray icon → **Settings** to customise:

### Input Device
Select which microphone to use. Defaults to your Windows system default recording device.

### Sounds
- **Enable/disable** all sounds
- **Start sound** — plays when recording begins (default: Speech On)
- **Done sound** — plays when text has been pasted, or when Win+\\ is pressed to stop early (default: Speech Off)
- **Error sound** — plays if something goes wrong (default: Windows Foreground)

All Windows system sounds from `C:\Windows\Media\` are available in the dropdown. Click the ▶ button to preview.

### Overlay Appearance
- **Font size** — adjust the transcript text size (8–24pt)
- **Background colour** — click the swatch to pick a colour
- **Text colour** — colour of the transcript text
- **Status colour** — colour of the status line ("Listening...", "Cleaning up...", etc.)
- **Background opacity** — how transparent the overlay background is (30–100%)

A live preview shows exactly how the overlay will look.

### AI Cleanup Prompt
This is the instruction sent to the AI that cleans up your dictated speech before pasting. The default prompt fixes punctuation, capitalisation, and sentence structure while preserving your tone.

You can customise this to change the style of the output. Examples:

- `"Rewrite in a professional, formal tone. Fix all grammar and punctuation."` — for business emails
- `"Keep it very casual and conversational. Fix obvious errors only."` — for chat messages
- `"Rewrite in British English with proper spelling and punctuation."` — for UK style
- `"Translate to Spanish and clean up grammar."` — for translation
- `"Convert to bullet points with proper formatting."` — for notes

All settings are saved to `settings.json` next to the application and persist across restarts.

## API Details

This app uses two Groq API endpoints:

| Endpoint | Model | Purpose |
|----------|-------|---------|
| `/openai/v1/audio/transcriptions` | `whisper-large-v3-turbo` | Speech-to-text transcription |
| `/openai/v1/chat/completions` | `llama-3.3-70b-versatile` | Text cleanup and polishing |

Groq offers a generous free tier. Check [console.groq.com](https://console.groq.com) for current rate limits.

## Troubleshooting

**No sound when pressing Win+\\**
- Check that the app is running in the system tray (look for the microphone icon, you may need to click the ^ arrow to see hidden icons)
- Another app may have registered Win+\\ — close other hotkey tools and retry

**Recording stops too quickly**
- The silence detection threshold may be too sensitive for your environment. The app waits for 2 seconds of silence and ignores the first 2 seconds entirely, but very quiet speech may be detected as silence
- Press **Win+\\** again to stop recording manually at any time

**"groq_key.txt not found" error**
- Make sure the file is in the same folder as the `.exe` or in your working directory when using `dotnet run`
- The file must contain only the API key with no extra whitespace or quotes

**Transcription is empty or wrong**
- Check your microphone is working in Windows Sound Settings
- Try selecting a specific input device in Settings instead of "(System default)"
- Ensure your Groq API key is valid and has not exceeded rate limits

**Text not pasting**
- The app simulates Ctrl+V — make sure the target application supports paste from clipboard
- Click into the text field where you want the text before pressing Win+\\

**Escape key not working**
- The Escape key is registered as a global hotkey while the app is running. If another app has claimed it, it may not respond. Try closing other hotkey-heavy applications.

## Licence

This application is provided as-is for personal use.


## Requirements

- **Windows 10/11** (x64)
- **.NET 8 SDK** — install via `winget install Microsoft.DotNet.SDK.8` or from [dot.net](https://dot.net)
- **Groq API key** — sign up free at [console.groq.com](https://console.groq.com) and generate an API key

## Setup

### 1. Get the code

Place these files in a folder (e.g. `C:\VoiceDictation\`):

```
VoiceDictation/
├── voice-dictation.csproj
├── voice-dictation-program.cs
├── groq_key.txt
└── README.md
```

### 2. Add your API key

Create a file called `groq_key.txt` in the same folder. Paste your Groq API key as the only content:

```
gsk_your_actual_key_here
```

No quotes, no extra lines — just the key.

### 3. Run in development mode

```
cd C:\VoiceDictation
dotnet run
```

The app will appear in your system tray. The first run may take a moment while NuGet restores packages.

### 4. Build a standalone executable (optional)

```
dotnet publish -c Release
```

The compiled binary will be at:

```
bin\Release\net8.0-windows\win-x64\publish\VoiceDictation.exe
```

Copy `groq_key.txt` into the same folder as the `.exe` before running it.

## Settings

Right-click the tray icon → **Settings** to customise:

### Input Device
Select which microphone to use. Defaults to your Windows system default recording device.

### Sounds
- **Enable/disable** all sounds
- **Start sound** — plays when recording begins (default: Speech On)
- **Done sound** — plays when text has been pasted (default: Speech Off)
- **Error sound** — plays if something goes wrong (default: Windows Foreground)

All Windows system sounds from `C:\Windows\Media\` are available in the dropdown. Click the ▶ button to preview.

### Overlay Appearance
- **Font size** — adjust the transcript text size (8–24pt)
- **Background colour** — click the swatch to pick a colour
- **Text colour** — colour of the transcript text
- **Status colour** — colour of the status line ("Listening...", "Cleaning up...", etc.)
- **Background opacity** — how transparent the overlay background is (30–100%)

A live preview shows exactly how the overlay will look.

### AI Cleanup Prompt
This is the instruction sent to the AI that cleans up your dictated speech before pasting. The default prompt fixes punctuation, capitalisation, and sentence structure while preserving your tone.

You can customise this to change the style of the output. Examples:

- `"Rewrite in a professional, formal tone. Fix all grammar and punctuation."` — for business emails
- `"Keep it very casual and conversational. Fix obvious errors only."` — for chat messages
- `"Rewrite in British English with proper spelling and punctuation."` — for UK style
- `"Translate to Spanish and clean up grammar."` — for translation
- `"Convert to bullet points with proper formatting."` — for notes

All settings are saved to `settings.json` next to the application and persist across restarts.

## API Details

This app uses two Groq API endpoints:

| Endpoint | Model | Purpose |
|----------|-------|---------|
| `/openai/v1/audio/transcriptions` | `whisper-large-v3-turbo` | Speech-to-text transcription |
| `/openai/v1/chat/completions` | `llama-3.3-70b-versatile` | Text cleanup and polishing |

Groq offers a generous free tier. Check [console.groq.com](https://console.groq.com) for current rate limits.

## Troubleshooting

**No sound when pressing Win+\\**
- Check that the app is running in the system tray (look for the microphone icon, you may need to click the ^ arrow to see hidden icons)
- Another app may have registered Win+\\ — close other hotkey tools and retry

**Recording stops too quickly**
- The silence detection threshold may be too sensitive for your environment. The app waits for 2 seconds of silence and ignores the first 2 seconds entirely, but very quiet speech may be detected as silence

**"groq_key.txt not found" error**
- Make sure the file is in the same folder as the `.exe` or in your working directory when using `dotnet run`
- The file must contain only the API key with no extra whitespace or quotes

**Transcription is empty or wrong**
- Check your microphone is working in Windows Sound Settings
- Try selecting a specific input device in Settings instead of "(System default)"
- Ensure your Groq API key is valid and has not exceeded rate limits

**Text not pasting**
- The app simulates Ctrl+V — make sure the target application supports paste from clipboard
- Click into the text field where you want the text before pressing Win+\\

## Licence

This application is provided as-is for personal use.
