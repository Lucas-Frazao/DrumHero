# Drum Hero

A Guitar Hero-style drum training application for Windows, designed for the **Alesis Nitro Max** electronic drum kit connected via USB MIDI.

Import your own `.flac` songs, and Drum Hero will separate the drums, generate a scrolling highway of notes, and give you real-time hit/miss feedback as you play along.

---

## Features

- **Song Import & Analysis**: Import `.flac` files → automatic drum separation (via Demucs) → drumless backing track + drum stem + note highway
- **Guitar Hero-style Highway**: 11-lane scrolling drum highway with color-coded notes for kick, snare, hi-hat, toms, crashes, and ride
- **Real-time MIDI Hit Detection**: Listens to your Alesis Nitro Max via USB MIDI with configurable timing windows (Easy ±80ms / Normal ±50ms / Hard ±30ms)
- **Tempo Control**: Slow songs down (25%, 50%, 75%) or speed up (125%) with pitch-preserving time stretch
- **Metronome & Count-in**: Built-in click track with 1 or 2 bar count-in
- **Performance Tracking**: End-of-session accuracy summary + full history per song
- **Low Latency**: WASAPI audio output + WPF CompositionTarget.Rendering for smooth 60fps animation

---

## Prerequisites

### Required
- **Windows 10/11** (x64)
- **Visual Studio 2022** (17.8+) with:
  - .NET 8.0 SDK
  - ".NET desktop development" workload (includes WPF)
- **Alesis Nitro Max** drum kit (or any USB MIDI drum controller)

### For Song Analysis (Drum Separation)
- **Python 3.8+** installed and on your system PATH
- **Demucs** (Facebook/Meta's music source separation):
  ```
  pip install demucs
  ```
- **librosa** (for drum transcription/onset detection):
  ```
  pip install librosa
  ```
- First analysis run will download the `htdemucs` model (~80MB)

> **Note**: If Python/Demucs is not installed, the app will fall back to a basic energy-based onset detection for transcription, but stem separation requires Demucs.

---

## Build & Run

### 1. Clone/Open the Solution
```
cd DrumHero
start DrumHero.sln
```

### 2. Restore NuGet Packages
Visual Studio will auto-restore on build, or run:
```
dotnet restore
```

### 3. Build
```
dotnet build --configuration Release
```

### 4. Run
```
dotnet run --project DrumHero
```

Or press F5 in Visual Studio.

---

## NuGet Packages Used

| Package | Version | Purpose |
|---------|---------|---------|
| NAudio | 2.2.1 | Audio playback (WASAPI), MIDI input |
| NAudio.Midi | 2.2.1 | MIDI event handling |
| SoundTouch.Net | 2.3.2 | Pitch-preserving time stretch |
| SoundTouch.Net.NAudioSupport | 2.3.1 | NAudio ↔ SoundTouch bridge |
| TagLibSharp | 2.3.0 | FLAC metadata reading |
| Microsoft.Data.Sqlite | 8.0.11 | SQLite database |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | DI container |
| Microsoft.Extensions.Hosting | 8.0.1 | Host builder |
| CommunityToolkit.Mvvm | 8.3.2 | MVVM source generators |
| System.Text.Json | 8.0.5 | JSON serialization |

---

## Setup Guide

### 1. Connect Your Drum Kit
1. Connect the Alesis Nitro Max module to your Surface Pro / PC via USB cable
2. Windows should recognize it as a USB MIDI device automatically
3. Open Drum Hero → Settings → select your MIDI device from the dropdown

### 2. Configure Audio Output
1. In Settings, select your preferred audio output device
2. Adjust the buffer size (lower = less latency, higher = fewer glitches; 50ms recommended)

### 3. Import a Song
1. Click **"+ Add Song"** on the Library screen
2. Select a `.flac` file from your computer
3. Wait for analysis to complete (typically 2–5 minutes depending on song length and GPU)
4. The song appears in your library with "Ready" status

### 4. Practice
1. Double-click a song or click **"▶ Practice"**
2. Press the play button to start
3. Notes scroll down the highway — hit them when they reach the white line
4. Green flash = hit, red flash = miss
5. When done, view your accuracy summary

---

## MIDI Mapping (Alesis Nitro Max)

Based on the official Alesis Nitro Max Drum Module User Guide v1.1, Section 5.2:

| Pad | MIDI Note | Highway Lane |
|-----|-----------|-------------|
| Kick | 36 | Kick |
| Snare (Head) | 38 | Snare |
| Snare (Rim) | 40 | Snare |
| Tom 1 | 48 | Rack Tom 1 |
| Tom 1 Rim | 50 | Rack Tom 1 |
| Tom 2 | 45 | Rack Tom 2 |
| Tom 2 Rim | 47 | Rack Tom 2 |
| Tom 3 (Floor) | 43 | Floor Tom |
| Tom 3 Rim | 58 | Floor Tom |
| Tom 4 (Expansion) | 41 | Floor Tom |
| Tom 4 Rim | 39 | Floor Tom |
| Hi-Hat Closed | 42 | Closed HH |
| Hi-Hat Pedal | 44 | Closed HH |
| Hi-Hat Open | 46 | Open HH |
| Hi-Hat Half-Open | 23 | Open HH |
| Hi-Hat Splash | 21 | Open HH |
| Crash 1 | 49 | Crash 1 |
| Crash 2 | 57 | Crash 2 |
| Crash 3 (Extra) | 55 | Crash 3 |
| Ride | 51 | Ride |
| Ride Bell | 53 | Ride |
| Ride Edge | 59 | Ride |

> The two extra cymbals you added are mapped to Crash 2 (MIDI 57) and Crash 3 (MIDI 55). If your module assigns different note numbers, you can adjust the mapping in `Models/MidiDrumMap.cs`.

---

## Architecture

```
DrumHero/
├── Models/              # Domain models (Song, HighwayNote, PracticeRun, etc.)
├── Persistence/         # SQLite repos, FileStorageService
├── Analysis/            # IAnalysisService, DemucsAnalysisService, HttpAnalysisService
├── Audio/               # AudioPlaybackEngine, MidiInputEngine, HitDetectionEngine, TimeStretch
├── Services/            # SongImportService, SongLibraryService, PracticeSessionService
├── Infrastructure/      # MVVM base classes, NavigationService
├── ViewModels/          # LibraryVM, PracticeVM, SettingsVM, AnalysisProgressVM
├── Views/               # WPF XAML views
│   └── Controls/        # DrumHighwayControl (custom renderer)
├── Converters/          # WPF value converters
├── App.xaml(.cs)        # Application entry, DI configuration
└── MainWindow.xaml(.cs) # Shell window with navigation ContentControl
```

**Pattern**: MVVM with dependency injection. ViewModels are resolved via `Microsoft.Extensions.DependencyInjection`. Navigation uses `DataTemplate` matching in `MainWindow.xaml`.

---

## Data Storage

All data is stored locally in:
```
%LocalAppData%\DrumHero\
├── drumhero.db          # SQLite database (songs, practice runs, settings)
└── Songs/
    └── {songId}/
        ├── drumless_track.wav
        ├── drum_stem.wav
        └── transcription.json
```

---

## Transcription JSON Format

The analysis service produces a JSON transcription:
```json
{
  "bpm": 120.0,
  "time_signature": { "numerator": 4, "denominator": 4 },
  "duration_seconds": 240.5,
  "notes": [
    {
      "time_seconds": 0.5,
      "midi_note": 42,
      "velocity": 0.8,
      "bar": 1,
      "beat": 1,
      "sub_beat": 1,
      "drum_type": "hihat_closed"
    }
  ]
}
```

---

## Switching Analysis Backends

### Local Demucs (Default)
No configuration needed if Python + Demucs are installed.

### HTTP Cloud API
To use a cloud API instead, edit `App.xaml.cs`:
```csharp
// Replace the Demucs line with:
services.AddSingleton<IAnalysisService>(sp => 
    new HttpAnalysisService("https://your-api-url.com", "your-api-key"));
```

The HTTP client expects:
- `POST /analyze` — upload .flac, returns `{ "job_id": "..." }`
- `GET /status/{jobId}` — returns `{ "status": "completed", "progress": 0.75 }`
- `GET /results/{jobId}/drumless` — download drumless WAV
- `GET /results/{jobId}/drums` — download drum stem WAV
- `GET /results/{jobId}/transcription` — download transcription JSON

---

## Troubleshooting

| Issue | Solution |
|-------|---------|
| No MIDI devices found | Ensure the Alesis module is connected via USB and powered on. Click "Refresh" in Settings. |
| Analysis fails | Ensure Python, Demucs, and librosa are installed. Run `python -m demucs --help` to verify. |
| High latency | Reduce buffer size in Settings (try 30ms). Close other audio applications. |
| Notes don't match my kit | Edit the MIDI mapping in `Models/MidiDrumMap.cs` to match your module's settings. Use a MIDI monitor tool like MIDIOX to check which note numbers your pads send. |
| Crashes on startup | Ensure .NET 8.0 runtime is installed: `dotnet --version` |

---

## License

This is a personal project. All third-party libraries are used under their respective open-source licenses.
