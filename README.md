# 🥁 DrumHero

A **Guitar Hero-style drum training application** for Windows, built for the **Alesis Nitro Max** electronic drum kit. Import any song, and DrumHero will separate the drums, generate a scrolling highway of notes, and give you real-time audio and visual feedback as you play along.

Built with .NET 8, WPF, and NAudio. No internet required after setup.

---

## Download & Install

### Option 1: Download the Release (Recommended)

1. Go to the [**Releases**](https://github.com/Lucas-Frazao/DrumHero/releases) page
2. Download **`DrumHero-win-x64.zip`** from the latest release
3. Extract the ZIP to any folder (e.g., `C:\DrumHero`)
4. Run **`DrumHero.exe`** — no installation needed

> **Note**: Windows SmartScreen may show a warning the first time you run it since the app is unsigned. Click **"More info"** → **"Run anyway"**.

### Option 2: Build from Source

See the [Build from Source](#build-from-source) section below.

---

## System Requirements

| Component | Requirement |
|-----------|------------|
| **OS** | Windows 10 or 11 (x64) |
| **RAM** | 4 GB minimum |
| **Drum Kit** | Alesis Nitro Max (or any USB MIDI drum controller) |
| **Connection** | USB MIDI cable from drum module to PC |
| **Audio Output** | Laptop speakers, headphones, or any audio device |
| **For Song Analysis** | Python 3.8+ with Demucs and librosa (see below) |

---

## Features

- **Song Import & Analysis** — Import `.flac` or `.mid` files. FLAC files go through automatic drum separation (via Demucs) to create a drumless backing track + note highway. MIDI files are mapped directly.
- **11-Lane Scrolling Highway** — Color-coded lanes for kick, snare, hi-hat (open/closed), 3 toms, 3 crashes, and ride, arranged to mirror the physical drum kit layout.
- **Real-Time MIDI Hit Detection** — Connects to your drum kit via USB MIDI. Configurable timing windows: Easy (±95ms), Normal (±65ms), Hard (±40ms).
- **Live Drum Audio Feedback** — Synthesized drum sounds play through your laptop speakers when you hit pads, with 16-voice polyphony and velocity sensitivity. Sound design inspired by Lars Ulrich's drum tone on Metallica's *…And Justice for All*.
- **Tempo Control** — Slow songs down (25%, 50%, 75%) or speed up (125%) with pitch-preserving time stretch. Highway speed syncs automatically.
- **Metronome & Count-In** — Built-in click track with 1 or 2 bar count-in before songs start.
- **Performance Tracking** — End-of-session accuracy breakdown + full practice history per song.
- **MIDI Debug Display** — Built-in MIDI note monitor (🔌 button) to see exactly which MIDI notes your pads send, useful for troubleshooting.
- **Low Latency** — WASAPI shared-mode audio output at 44.1 kHz stereo, 50ms buffer. WPF CompositionTarget.Rendering for smooth 60fps highway animation.

---

## Quick Start

### 1. Connect Your Drum Kit

1. Connect the **Alesis Nitro Max** module to your PC via **USB MIDI cable**
2. Power on the drum module
3. Windows should recognize it as a USB MIDI device automatically (no drivers needed)

### 2. Launch DrumHero

1. Run `DrumHero.exe`
2. The app will detect your MIDI device automatically
3. If multiple MIDI devices are connected, go to **Settings** and select the correct one

### 3. Set Up Song Analysis (First Time Only)

To import `.flac` songs with automatic drum separation, you need Python with two packages:

```
pip install demucs librosa
```

> The first song analysis will download the `htdemucs` AI model (~80 MB). After that, analysis runs offline.
>
> If Python is not installed, you can still import `.mid` (MIDI) files directly — they don't require analysis.

### 4. Import a Song

1. Click **"+ Add Song"** on the Library screen
2. Choose a `.flac` file or `.mid` file
3. For FLAC files, wait for analysis to complete (typically 2–5 minutes depending on song length)
4. The song appears in your library with "Ready" status

### 5. Practice

1. Select a song and click **"▶ Practice"**
2. Press play — notes scroll down the highway toward the hit line
3. Hit the correct pad when the note reaches the white line
4. **Green flash** = hit, **Red flash** = miss
5. When done, review your accuracy summary

---

## Highway Lane Layout

The highway lanes are arranged left-to-right to mirror the physical drum kit from the **drummer's perspective**:

```
 HH-C │ HH-O │ CR1 │ SNARE │ KICK │ TOM1 │ TOM2 │ FLOOR │ CR2 │ RIDE │ CR3
 teal  │ blue │ plum│ gold  │ org  │ sage │ mint  │ l.gold│ orch│ sky  │ m.orch
◄── left hand side ──────────── center ────────────── right hand side ──►
```

| Lane | Color | Position Rationale |
|------|-------|--------------------|
| HH-C (Closed Hi-Hat) | Teal | Far left — hi-hat is leftmost on the kit |
| HH-O (Open Hi-Hat) | Light Blue | Next to closed hi-hat |
| CR1 (Crash 1) | Plum | Left crash, above hi-hat area |
| SNARE | Gold | Center-left — right in front of the drummer |
| KICK | Orange | Center — pedal, directly under snare |
| TOM1 (Rack Tom 1) | Sage | Center-right — mounted above kick |
| TOM2 (Rack Tom 2) | Mint | Right of Tom 1 |
| FLOOR (Floor Tom) | Light Gold | Right side — floor tom position |
| CR2 (Crash 2) | Orchid | Right crash, above floor tom area |
| RIDE | Sky Blue | Far right — ride cymbal position |
| CR3 (Crash 3) | Medium Orchid | Far right — extra crash / china |

---

## Drum Kit Specification

### Supported Hardware

**Primary**: Alesis Nitro Max electronic drum kit  
**Connection**: USB MIDI cable from drum module to PC (no audio interface needed)  
**Audio output**: Laptop speakers, headphones, or any Windows audio device

The app will work with any USB MIDI drum controller, but the MIDI note mapping is configured for the Alesis Nitro Max. Other kits may need mapping adjustments (see [Custom MIDI Mapping](#custom-midi-mapping)).

### MIDI Note Mapping (Alesis Nitro Max)

The following table shows which MIDI note each pad sends and which highway lane it maps to:

| Pad | MIDI Note(s) | Highway Lane |
|-----|-------------|-------------|
| **Kick** | 36, 35 | KICK |
| **Snare (Head)** | 38 | SNARE |
| **Snare (Rim)** | 40 | SNARE |
| **Side Stick** | 37 | SNARE |
| **Rack Tom 1 (High)** | 48, 50, 47 | TOM1 |
| **Rack Tom 2 (Low)** | 45, 43 | TOM2 |
| **Floor Tom** | 41, 58 | FLOOR |
| **Hi-Hat Closed** | 42, 44 (pedal) | HH-C |
| **Hi-Hat Open** | 46 | HH-O |
| **Hi-Hat Half-Open** | 23 | HH-O |
| **Hi-Hat Splash** | 21 | HH-O |
| **Crash 1** | 49 | CR1 |
| **Crash 2** | 57 | CR2 |
| **Crash 3 / Splash** | 55, 52 | CR3 |
| **Ride** | 51 | RIDE |
| **Ride Bell** | 53 | RIDE |
| **Ride Edge** | 59 | RIDE |

### General MIDI Percussion Fallback

Songs from Songsterr or other MIDI sources may use General MIDI percussion note numbers for instruments not on your kit (bongos, congas, timbales, cowbell, etc.). DrumHero automatically maps these to the closest equivalent pad:

| GM Instrument (Note) | Maps To |
|----------------------|---------|
| Tambourine (54), Cowbell (56) | Closed Hi-Hat |
| Bongos (60-61), Congas (62-64) | Toms |
| Timbales (65-66) | Toms |
| Agogo, Cabasa, Maracas, etc. | Closed Hi-Hat |
| Claves (75) | Snare (Side Stick) |
| Wood Blocks (76-77) | Toms |
| Triangle (80-81) | Hi-Hat |

### Drum Sound Design

DrumHero plays synthesized drum sounds through your speakers when you hit pads, providing instant audio feedback even without monitoring through the drum module. The sounds are inspired by Lars Ulrich's tone on *…And Justice for All* (1988):

| Drum | Character |
|------|-----------|
| **Kick** | Massive, bass-dominant thump. Deep sub-bass (40–55 Hz) with layered harmonics, heavy saturation, and long sustain. Minimal beater click. |
| **Snare** | Thin, bright, papery crack. Prominent snare wire buzz, minimal body, fast gated decay. |
| **Rack Tom 1** | High-pitched (170 Hz), tight 0.22s sustain, deep pitch drop on attack. |
| **Rack Tom 2** | Mid-pitched (120 Hz), 0.26s sustain, gated and punchy. |
| **Floor Tom** | Deep (75 Hz), boomy 0.32s sustain. |
| **Crashes** | Bright, splashy, and aggressive with filtered white noise. |
| **Ride** | Bright ping with bell-like sustain. |
| **Hi-Hats** | Cutting and tight (closed) or shimmering (open). |

All samples are pre-computed at startup for zero-latency playback. 16-voice polyphony ensures simultaneous hits are never dropped.

---

## Settings & Configuration

Access settings from the **Settings** view in the app:

| Setting | Default | Description |
|---------|---------|-------------|
| MIDI Input Device | Auto-detected | Select your drum module |
| Audio Output Device | System default | Select speaker/headphone output |
| Difficulty | Normal (±65ms) | Hit timing window: Easy ±95ms, Normal ±65ms, Hard ±40ms |
| Audio Buffer | 50ms | Lower = less latency (may glitch), higher = more stable |
| Backing Track Volume | 80% | Volume of the drumless backing track |
| Drum Feedback Volume | 100% | Volume of synthesized drum sounds |
| Drum Stem Volume | 0% (muted) | Original drum track from the song (muted for practice) |
| Metronome | Off | Enable click track with configurable count-in (0, 1, or 2 bars) |
| Input Timing Offset | 45ms | Compensates for USB MIDI + audio latency |

---

## Custom MIDI Mapping

If you use a different drum kit or have a non-standard module configuration, you can adjust the MIDI mapping by editing `Models/MidiDrumMap.cs` in the source code:

1. Use the built-in **MIDI Debug Display** (🔌 button in the practice view) to see which MIDI note each pad sends
2. Edit the `_noteToLane` dictionary in `MidiDrumMap.cs` to match your kit
3. Rebuild the app

Example — adding a second kick pedal on MIDI note 33:
```csharp
{ 33, DrumLane.RightKick },  // Second kick pedal
```

---

## Build from Source

### Prerequisites

- **Windows 10/11** (x64)
- [**.NET 8.0 SDK**](https://dotnet.microsoft.com/download/dotnet/8.0) (8.0.x)
- **Visual Studio 2022** (17.8+) with the **".NET desktop development"** workload, or just the .NET SDK for command-line builds

### Clone & Build

```bash
git clone https://github.com/Lucas-Frazao/DrumHero.git
cd DrumHero
dotnet restore DrumHero/DrumHero.csproj
dotnet build DrumHero/DrumHero.csproj --configuration Release
```

### Run

```bash
dotnet run --project DrumHero/DrumHero.csproj
```

Or open `DrumHero.sln` in Visual Studio and press F5.

### Publish a Self-Contained EXE

```bash
dotnet publish DrumHero/DrumHero.csproj ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  --output ./publish
```

This produces a single `DrumHero.exe` in the `./publish` folder that includes the .NET runtime and all dependencies. No installation needed on the target machine.

---

## NuGet Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| NAudio | 2.2.1 | Audio playback (WASAPI), MIDI input |
| NAudio.Midi | 2.2.1 | MIDI event handling |
| SoundTouch.Net | 2.3.2 | Pitch-preserving time stretch |
| SoundTouch.Net.NAudioSupport | 2.3.1 | NAudio ↔ SoundTouch bridge |
| TagLibSharp | 2.3.0 | FLAC metadata reading |
| Microsoft.Data.Sqlite | 8.0.11 | SQLite database for persistence |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | DI container |
| Microsoft.Extensions.Hosting | 8.0.1 | Host builder |
| CommunityToolkit.Mvvm | 8.3.2 | MVVM source generators & commands |
| System.Text.Json | 8.0.5 | JSON serialization |

---

## Architecture

```
DrumHero/
├── Analysis/            # Song analysis: Demucs stem separation, MIDI transcription
├── Audio/               # Audio engine: playback, MIDI input, hit detection, time stretch,
│                        #   drum sound synthesis, polyphonic feedback
├── Converters/          # WPF value converters
├── Infrastructure/      # MVVM base classes, navigation service, async relay commands
├── Models/              # Domain models: Song, HighwayNote, DrumLane, MidiDrumMap,
│                        #   PracticeRun, DifficultyLevel, AppSettings
├── Persistence/         # SQLite repositories, file storage service
├── Services/            # Song import, library management, practice session orchestration
├── ViewModels/          # Library, Practice, Settings, AnalysisProgress view models
├── Views/               # WPF XAML views
│   └── Controls/        # DrumHighwayControl (custom WPF renderer, 60fps)
├── App.xaml(.cs)        # Application entry point, DI container configuration
└── MainWindow.xaml(.cs) # Shell window with navigation via DataTemplate matching
```

**Design Pattern**: MVVM with constructor-injected dependencies. `AudioPlaybackEngine` is a singleton (WASAPI stays running for always-on drum feedback). Navigation uses `ContentControl` with `DataTemplate` matching in `MainWindow.xaml`.

### Data Storage

All user data is stored locally:
```
%LocalAppData%\DrumHero\
├── drumhero.db              # SQLite database (songs, practice runs, settings)
└── Songs/
    └── {songId}/
        ├── drumless_track.wav    # Backing track with drums removed
        ├── drum_stem.wav         # Isolated drum track
        └── transcription.json    # Note-by-note timing data
```

---

## Troubleshooting

| Issue | Solution |
|-------|---------|
| **No MIDI devices found** | Ensure the Alesis module is connected via USB and powered on. Go to Settings and click Refresh. |
| **Pads not registering hits** | Use the 🔌 MIDI Debug button to verify the app is receiving MIDI notes. Check that the correct MIDI device is selected in Settings. |
| **Wrong pad sounds** | Use 🔌 MIDI Debug to check note numbers, then compare with the [MIDI Note Mapping](#midi-note-mapping-alesis-nitro-max) table. |
| **Song analysis fails** | Ensure Python, Demucs, and librosa are installed. Run `python -m demucs --help` to verify. |
| **High audio latency** | Reduce the audio buffer in Settings (try 30ms). Close other audio applications. |
| **Notes don't align with music** | Adjust the Input Timing Offset in Settings (default 45ms). Increase if your hits register early, decrease if late. |
| **App crashes on startup** | Ensure .NET 8.0 runtime is installed. Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0). If using the self-contained .exe, this is not needed. |
| **SmartScreen blocks the app** | Click "More info" → "Run anyway". The app is unsigned but safe. |

---

## License

Personal project by Lucas Frazao. All third-party libraries are used under their respective open-source licenses.
