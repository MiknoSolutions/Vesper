# VESPER

Lightweight, portable Windows desktop application for **speech-to-text** recognition powered by [Whisper AI](https://github.com/openai/whisper).

## Features

- **Three recognition modes:**
  - **Toggle** — press hotkey to start recording, press again to stop → text is pasted at cursor
  - **Push-to-Talk** — hold hotkey to record, release to transcribe → text is pasted at cursor
  - **Continuous** — always listening, automatically detects speech and pastes text in real-time
- **Dual backend** — run Whisper locally (offline, free) or via OpenAI API (online)
- **Configurable hotkeys** — set any key combination through the GUI
- **Multi-language** — auto-detection or manual language selection (16+ languages)
- **System tray** — runs in background, always ready
- **Portable** — single .exe, no installation required
- **Settings** — stored in `settings.json` next to the executable

## Requirements

- Windows 10/11 (x64)
- Microphone
- For local mode: downloaded Whisper model (via built-in downloader)
- For API mode: OpenAI API key

## Quick Start

1. Download `Vesper.exe` from Releases
2. Run the application
3. Open **Settings** and choose:
   - **Backend**: Local (offline) or OpenAI API
   - If Local: select model size and click **Download**
   - If API: enter your OpenAI API key
   - Set your preferred **hotkey** (default: `Ctrl+Shift+R`)
   - Select **microphone** and **language**
4. Choose a recognition mode (Toggle / Push-to-Talk / Continuous)
5. Click in any text field, press your hotkey, and speak

## Google Cloud For End Users

If you use Google Cloud Speech-to-Text, every user should provide their own credentials.

1. In Google Cloud Console, create a service account with Speech-to-Text access.
2. Download its JSON key file.
3. In Vesper: Settings -> Speech Backend -> Google Cloud Speech-to-Text -> Browse...
4. Select the JSON file.

Vesper copies that file into the current Windows user's AppData folder and stores only the local path in settings.
Do not ship your own production key inside the repository or releases.

## Building from Source

```bash
# Clone
git clone https://github.com/MiknoSolutions/VESPER.git
cd VESPER/src

# Build
dotnet build

# Publish portable .exe
dotnet publish Vesper/Vesper.csproj -c Release -r win-x64 --self-contained
```

The output will be in `src/Vesper/bin/Release/net8.0-windows/win-x64/publish/`.

## Release Packaging

1. Build portable executable:
  - `powershell -ExecutionPolicy Bypass -File scripts/build-portable.ps1`
2. Create release zip and checksum:
  - `powershell -ExecutionPolicy Bypass -File scripts/package-release.ps1 -Version v1.0.0`
3. Upload files from `release/` to GitHub Releases.

Templates and user docs:
- `docs/RELEASE_NOTES_TEMPLATE.md`
- `docs/END_USER_GUIDE.md`

## Tech Stack

- **C# / .NET 8** (WPF)
- **Whisper.net** — local Whisper inference (whisper.cpp wrapper)
- **NAudio** — microphone audio capture
- **Hardcodet.NotifyIcon.Wpf** — system tray integration

## Project Structure

```
src/Vesper/
├── Models/          — AppSettings, enums
├── Services/
│   ├── Audio/       — Microphone capture, Voice Activity Detection
│   ├── Recognition/ — Local Whisper + OpenAI API backends
│   └── Input/       — Global hotkeys, text injection (clipboard + paste)
├── ViewModels/      — MVVM view models
├── Views/           — Settings window
└── Helpers/         — P/Invoke, XAML converters

scripts/
├── build-portable.ps1
└── package-release.ps1

docs/
├── END_USER_GUIDE.md
└── RELEASE_NOTES_TEMPLATE.md
```

## License

MIT