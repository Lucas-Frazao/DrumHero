using DrumHero.Models;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DrumHero.Analysis;

/// <summary>
/// Analysis service that uses a local Demucs installation (Python) for stem separation
/// and a basic onset-detection approach for drum transcription.
/// 
/// Prerequisites:
/// - Python 3.8+ installed and on PATH
/// - Demucs installed: pip install demucs
/// - The first run will download the htdemucs model (~80MB)
/// </summary>
public class DemucsAnalysisService : IAnalysisService
{
    private readonly string _pythonPath;
    private readonly string _modelName;

    public DemucsAnalysisService(string pythonPath = "python", string modelName = "htdemucs")
    {
        _pythonPath = pythonPath;
        _modelName = modelName;
    }

    public async Task<AnalysisResult> AnalyzeAsync(
        string flacFilePath,
        string outputDirectory,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new AnalysisResult();

        try
        {
            // Stage 0: Convert FLAC to WAV if needed
            var audioPath = flacFilePath;
            if (flacFilePath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(new AnalysisProgress
                {
                    Stage = "Converting",
                    ProgressFraction = 0.05,
                    Message = "Converting FLAC to WAV..."
                });

                var wavPath = Path.Combine(outputDirectory, "input.wav");
                var converted = await ConvertFlacToWavAsync(flacFilePath, wavPath, cancellationToken);
                if (!converted)
                {
                    result.Success = false;
                    result.ErrorMessage = "Failed to convert FLAC to WAV. Make sure librosa and soundfile are installed.";
                    return result;
                }
                audioPath = wavPath;
            }

            // Stage 1: Run Demucs separation
            progress?.Report(new AnalysisProgress
            {
                Stage = "Separating",
                ProgressFraction = 0.1,
                Message = "Running Demucs stem separation..."
            });

            var demucsOutputDir = Path.Combine(outputDirectory, "demucs_output");
            Directory.CreateDirectory(demucsOutputDir);

            // Run demucs CLI
            var demucsArgs = $"-m demucs.separate --two-stems drums -n {_modelName} " +
                             $"-o \"{demucsOutputDir}\" \"{audioPath}\"";

            var processInfo = new ProcessStartInfo
            {
                FileName = _pythonPath,
                Arguments = demucsArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                result.Success = false;
                result.ErrorMessage = "Failed to start Demucs process. Ensure Python and Demucs are installed.";
                return result;
            }

            // Capture stderr and stdout
            var stderrLines = new List<string>();
            var stdoutLines = new List<string>();

            // Monitor progress from stderr (Demucs outputs progress there)
            var stderrTask = Task.Run(async () =>
            {
                using var reader = process.StandardError;
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (line != null)
                    {
                        stderrLines.Add(line);
                        if (line.Contains("%"))
                        {
                            // Try to parse progress percentage
                            var pctStr = line.Split('%')[0].Trim();
                            var lastSpace = pctStr.LastIndexOf(' ');
                            if (lastSpace >= 0) pctStr = pctStr[(lastSpace + 1)..];
                            if (double.TryParse(pctStr, out var pct))
                            {
                                progress?.Report(new AnalysisProgress
                                {
                                    Stage = "Separating",
                                    ProgressFraction = 0.1 + (pct / 100.0) * 0.6,
                                    Message = $"Separating stems... {pct:F0}%"
                                });
                            }
                        }
                    }
                }
            }, cancellationToken);

            var stdoutTask = Task.Run(async () =>
            {
                using var reader = process.StandardOutput;
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (line != null)
                        stdoutLines.Add(line);
                }
            }, cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stderrTask, stdoutTask);

            string BuildProcessDetails()
            {
                var stderr = string.Join("\n", stderrLines.Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(40));
                var stdout = string.Join("\n", stdoutLines.Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(20));
                var details = $"Python: {_pythonPath}\nArgs: {demucsArgs}";
                if (!string.IsNullOrWhiteSpace(stderr)) details += $"\n\nSTDERR:\n{stderr}";
                if (!string.IsNullOrWhiteSpace(stdout)) details += $"\n\nSTDOUT:\n{stdout}";
                return details;
            }

            if (process.ExitCode != 0)
            {
                var demucsDetails = BuildProcessDetails();

                // Demucs failed. Try a librosa HPSS fallback to still create drum/non-drum stems.
                progress?.Report(new AnalysisProgress
                {
                    Stage = "Fallback",
                    ProgressFraction = 0.75,
                    Message = "Demucs failed, trying fallback stem separation..."
                });

                var fallbackDrumsPath = Path.Combine(outputDirectory, "drum_stem.wav");
                var fallbackDrumlessPath = Path.Combine(outputDirectory, "drumless_track.wav");
                var separated = await CreateFallbackStemSeparationAsync(
                    audioPath,
                    fallbackDrumsPath,
                    fallbackDrumlessPath,
                    cancellationToken);

                if (!separated)
                {
                    result.Success = false;
                    result.ErrorMessage =
                        "Demucs separation failed and fallback separation also failed.\n\n" + demucsDetails;
                    return result;
                }

                progress?.Report(new AnalysisProgress
                {
                    Stage = "Fallback",
                    ProgressFraction = 0.8,
                    Message = "Demucs failed; using fallback separation."
                });

                var fallbackTranscriptionPath = Path.Combine(outputDirectory, "transcription.json");
                await GenerateTranscriptionAsync(fallbackDrumsPath, fallbackTranscriptionPath, progress, cancellationToken);

                result.Success = true;
                result.DrumlessTrackPath = fallbackDrumlessPath;
                result.DrumStemPath = fallbackDrumsPath;
                result.TranscriptionJsonPath = fallbackTranscriptionPath;
                return result;
            }

            // Check if output directory structure exists
            var demucsTrackDir = Directory.GetDirectories(demucsOutputDir).FirstOrDefault();
            if (demucsTrackDir == null)
            {
                result.Success = false;
                result.ErrorMessage =
                    "Demucs did not create output directory.\n\n" + BuildProcessDetails();
                return result;
            }

            var separatedDir = Directory.GetDirectories(demucsTrackDir).FirstOrDefault();
            if (separatedDir == null)
            {
                result.Success = false;
                result.ErrorMessage =
                    $"Demucs did not create model output directory at {demucsTrackDir}\n\n" + BuildProcessDetails();
                return result;
            }

            var drumsPath = Path.Combine(separatedDir, "drums.wav");
            var noDrumsPath = Path.Combine(separatedDir, "no_drums.wav");

            if (!File.Exists(drumsPath) || !File.Exists(noDrumsPath))
            {
                result.Success = false;
                result.ErrorMessage =
                    $"Demucs output files not found. Expected at: {separatedDir}\n\n" + BuildProcessDetails();
                return result;
            }

            // Copy to final locations
            var finalDrumsPath = Path.Combine(outputDirectory, "drum_stem.wav");
            var finalDrumlessPath = Path.Combine(outputDirectory, "drumless_track.wav");

            File.Copy(drumsPath, finalDrumsPath, overwrite: true);
            File.Copy(noDrumsPath, finalDrumlessPath, overwrite: true);

            progress?.Report(new AnalysisProgress
            {
                Stage = "Separating",
                ProgressFraction = 0.7,
                Message = "Stem separation complete."
            });

            // Stage 2: Generate transcription
            progress?.Report(new AnalysisProgress
            {
                Stage = "Transcribing",
                ProgressFraction = 0.75,
                Message = "Generating drum transcription..."
            });

            var transcriptionPath = Path.Combine(outputDirectory, "transcription.json");
            await GenerateTranscriptionAsync(finalDrumsPath, transcriptionPath, progress, cancellationToken);

            // Clean up demucs temp directory
            try { Directory.Delete(demucsOutputDir, true); } catch { }

            result.Success = true;
            result.DrumlessTrackPath = finalDrumlessPath;
            result.DrumStemPath = finalDrumsPath;
            result.TranscriptionJsonPath = transcriptionPath;

            progress?.Report(new AnalysisProgress
            {
                Stage = "Complete",
                ProgressFraction = 1.0,
                Message = "Analysis complete!"
            });
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Analysis was cancelled.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Analysis failed: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Generates a drum transcription JSON by running a Python script that does
    /// onset detection + drum classification on the separated drum stem.
    /// </summary>
    private async Task GenerateTranscriptionAsync(
        string drumStemPath,
        string outputJsonPath,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Create a Python script for onset detection using librosa
        var scriptPath = Path.Combine(Path.GetTempPath(), "drum_transcribe.py");
        var pythonScript = GetTranscriptionScript();
        await File.WriteAllTextAsync(scriptPath, pythonScript, cancellationToken);

        var args = $"\"{scriptPath}\" \"{drumStemPath}\" \"{outputJsonPath}\"";

        var processInfo = new ProcessStartInfo
        {
            FileName = _pythonPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            // If Python transcription fails, create a minimal transcription
            await CreateFallbackTranscriptionAsync(drumStemPath, outputJsonPath);
            return;
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || !File.Exists(outputJsonPath))
        {
            // Fallback: create basic transcription from audio analysis
            await CreateFallbackTranscriptionAsync(drumStemPath, outputJsonPath);
        }

        progress?.Report(new AnalysisProgress
        {
            Stage = "Transcribing",
            ProgressFraction = 0.95,
            Message = "Transcription generated."
        });
    }

    private static string GetTranscriptionScript() => @"
import sys
import json
import numpy as np

try:
    import librosa
except ImportError:
    print('librosa not installed', file=sys.stderr)
    sys.exit(1)

from scipy.ndimage import maximum_filter1d
from scipy.signal import find_peaks


# --- NMF with semi-adaptive bases (KL divergence) ---
# Based on Dittmar et al. (DAFx 2014): semi-adaptive B yields
# less cross-talk than fully adaptive while preserving transients.
def _nmf_semi_adaptive(V, W_prior, n_iter=200, beta_exp=2.0):
    # V: (n_mels, n_frames) non-negative input
    # W_prior: (n_mels, n_components) prior templates from isolated drums
    # beta_exp: controls how fast adaptation kicks in (higher = slower)
    eps = 1e-10
    n_components = W_prior.shape[1]
    n_frames = V.shape[1]

    W = W_prior.copy()
    H = np.abs(np.random.RandomState(42).rand(n_components, n_frames)) + 0.1

    for k in range(n_iter):
        WH = W @ H + eps

        # Update H (activations)
        numerator_h = W.T @ (V / WH)
        denominator_h = W.sum(axis=0)[:, np.newaxis] + eps
        H *= numerator_h / denominator_h

        # Update W with semi-adaptive blending:
        # Early iterations stay close to prior; later iterations adapt more.
        # alpha = (1 - k/K)^beta  ->  starts at 1.0, decays to 0.0
        alpha = (1.0 - k / n_iter) ** beta_exp

        numerator_w = (V / WH) @ H.T
        denominator_w = H.sum(axis=1)[np.newaxis, :] + eps
        W_free = W * (numerator_w / denominator_w)

        # Blend: early = mostly prior, late = mostly adapted
        W = alpha * W_prior + (1.0 - alpha) * W_free

        # Re-normalize W columns
        col_sums = W.sum(axis=0, keepdims=True) + eps
        W /= col_sums
        H *= col_sums.T

    return W, H


# --- Drum spectral template initialization ---
def _make_templates(n_mels, sr, fmin, fmax):
    mel_freqs = librosa.mel_frequencies(n_mels=n_mels, fmin=fmin, fmax=fmax)

    def gauss(freqs, center, width):
        return np.exp(-0.5 * ((freqs - center) / width) ** 2)

    templates = {}

    # Kick: strong 50-100 Hz fundamental, body at 150 Hz
    t = gauss(mel_freqs, 60, 25) + 0.5 * gauss(mel_freqs, 130, 50)
    templates['kick'] = t / (t.max() + 1e-10)

    # Snare: 200 Hz body + 2-5 kHz snare wires (broadband)
    t = 0.5 * gauss(mel_freqs, 200, 80) + gauss(mel_freqs, 3500, 1200) + 0.3 * gauss(mel_freqs, 7000, 1500)
    templates['snare'] = t / (t.max() + 1e-10)

    # Hi-hat: high-frequency noise, 6-15 kHz
    t = gauss(mel_freqs, 8000, 2500) + 0.8 * gauss(mel_freqs, 12000, 2500)
    templates['hihat'] = t / (t.max() + 1e-10)

    # Tom (rack): mid-range tonal, 250-500 Hz
    t = gauss(mel_freqs, 350, 100) + 0.3 * gauss(mel_freqs, 700, 150)
    templates['tom'] = t / (t.max() + 1e-10)

    # Floor tom: lower tonal, 80-200 Hz, distinct from kick by
    # having more energy at 200-300 Hz and less sub-bass
    t = 0.4 * gauss(mel_freqs, 100, 30) + gauss(mel_freqs, 220, 70) + 0.3 * gauss(mel_freqs, 400, 100)
    templates['floor_tom'] = t / (t.max() + 1e-10)

    # Crash: very broadband high-frequency shimmer
    t = 0.2 * gauss(mel_freqs, 2000, 800) + 0.6 * gauss(mel_freqs, 6000, 2500) + gauss(mel_freqs, 10000, 3500)
    templates['crash'] = t / (t.max() + 1e-10)

    return templates


def _onset_envelope_peaks(y, sr, hop_length):
    # Use librosa onset detection as a timing reference.
    # Returns onset times in seconds.
    onset_env = librosa.onset.onset_strength(y=y, sr=sr, hop_length=hop_length)
    onset_frames = librosa.onset.onset_detect(
        onset_envelope=onset_env,
        sr=sr,
        hop_length=hop_length,
        backtrack=False,
        units='frames'
    )
    return set(onset_frames.tolist()), onset_env


def _peak_pick_with_onset_gate(activation, onset_frames_set, onset_env,
                                threshold_ratio, min_distance_frames,
                                gate_radius=3):
    # Only accept activation peaks that are near a detected onset.
    # This drastically reduces false positives from cross-talk.
    if len(activation) == 0:
        return np.array([], dtype=int)

    window = max(len(activation) // 20, 50)
    local_max = maximum_filter1d(activation, size=window)
    global_thresh = threshold_ratio * np.percentile(activation, 90)
    threshold = np.maximum(threshold_ratio * local_max, global_thresh)

    peaks = []
    for i in range(1, len(activation) - 1):
        if activation[i] > threshold[i]:
            if activation[i] >= activation[i - 1] and activation[i] >= activation[i + 1]:
                # Gate: only accept if near an onset event
                near_onset = False
                for offset in range(-gate_radius, gate_radius + 1):
                    if (i + offset) in onset_frames_set:
                        near_onset = True
                        break
                if not near_onset:
                    continue
                if len(peaks) == 0 or (i - peaks[-1]) >= min_distance_frames:
                    peaks.append(i)

    return np.array(peaks, dtype=int)


def _suppress_cross_talk(activations_dict, drum_names):
    # When overlapping drums fire at the same frame,
    # suppress the weaker ones if they look like cross-talk.
    # Kick vs floor_tom and hihat vs crash are the main offenders.
    conflict_groups = [
        ('kick', 'floor_tom'),
        ('hihat', 'crash'),
    ]

    for d1, d2 in conflict_groups:
        if d1 not in activations_dict or d2 not in activations_dict:
            continue
        a1 = activations_dict[d1]
        a2 = activations_dict[d2]
        n = min(len(a1), len(a2))
        for i in range(n):
            if a1[i] > 0 and a2[i] > 0:
                # Suppress the weaker by ratio
                ratio = min(a1[i], a2[i]) / (max(a1[i], a2[i]) + 1e-10)
                if ratio < 0.6:
                    # The weaker one is likely cross-talk; zero it out
                    if a1[i] > a2[i]:
                        a2[i] = 0.0
                    else:
                        a1[i] = 0.0

    return activations_dict


def _enforce_max_simultaneous(notes, max_simultaneous=4, time_tolerance=0.025):
    # A drummer has 2 hands + 2 feet = 4 max simultaneous hits.
    # When more than 4 notes land at the same time, keep only
    # the 4 with highest velocity.
    if len(notes) == 0:
        return notes

    notes.sort(key=lambda n: n['time_seconds'])

    # Group notes by approximate time
    groups = []
    current_group = [notes[0]]
    for n in notes[1:]:
        if abs(n['time_seconds'] - current_group[0]['time_seconds']) <= time_tolerance:
            current_group.append(n)
        else:
            groups.append(current_group)
            current_group = [n]
    groups.append(current_group)

    filtered = []
    for group in groups:
        if len(group) <= max_simultaneous:
            filtered.extend(group)
        else:
            # Keep top N by velocity
            group.sort(key=lambda n: n['velocity'], reverse=True)
            filtered.extend(group[:max_simultaneous])

    filtered.sort(key=lambda n: n['time_seconds'])
    return filtered


def _apply_musical_rules(notes, tempo):
    # Remove notes that violate basic musical constraints.
    # 1. Cannot have two kicks within 60ms (humanly impossible)
    # 2. Cannot have two snares within 60ms
    # 3. Hi-hat should not fire on exact same frame as crash
    #    (usually it is one or the other)
    min_same_drum_gap = 0.060  # 60ms

    # Sort by time
    notes.sort(key=lambda n: (n['time_seconds'], n['drum_type']))

    filtered = []
    last_time_by_drum = {}

    for note in notes:
        dt = note['drum_type']
        t = note['time_seconds']
        last_t = last_time_by_drum.get(dt, -1.0)

        if (t - last_t) < min_same_drum_gap:
            continue

        filtered.append(note)
        last_time_by_drum[dt] = t

    # Remove hihat when crash fires at same time
    crash_times = set()
    for n in filtered:
        if n['drum_type'] == 'crash':
            crash_times.add(round(n['time_seconds'], 4))

    result = []
    for n in filtered:
        if n['drum_type'] == 'hihat':
            t_rounded = round(n['time_seconds'], 4)
            if t_rounded in crash_times:
                continue
        result.append(n)

    return result


def transcribe_drums(audio_path, output_path):
    try:
        y, sr = librosa.load(audio_path, sr=44100, mono=True)
        duration = librosa.get_duration(y=y, sr=sr)

        # Tempo detection
        tempo, _ = librosa.beat.beat_track(y=y, sr=sr)
        if hasattr(tempo, '__len__'):
            tempo = float(tempo[0])
        else:
            tempo = float(tempo)

        # --- Mel spectrogram ---
        n_fft = 2048
        hop_length = 512
        n_mels = 128
        fmin = 20.0
        fmax = 16000.0

        S = librosa.feature.melspectrogram(
            y=y, sr=sr, n_fft=n_fft, hop_length=hop_length,
            n_mels=n_mels, fmin=fmin, fmax=fmax, power=2.0
        )
        S_nn = S + 1e-10

        # --- Onset detection for timing gate ---
        onset_frames_set, onset_env = _onset_envelope_peaks(y, sr, hop_length)

        # --- Build initial templates ---
        templates = _make_templates(n_mels, sr, fmin, fmax)
        drum_names = ['kick', 'snare', 'hihat', 'tom', 'floor_tom', 'crash']
        n_components = len(drum_names)

        W_prior = np.column_stack([templates[name] for name in drum_names])
        W_prior = W_prior + 0.01 * np.random.RandomState(42).rand(*W_prior.shape)
        W_prior /= (W_prior.sum(axis=0, keepdims=True) + 1e-10)

        # --- Run semi-adaptive NMF ---
        W, H = _nmf_semi_adaptive(S_nn, W_prior, n_iter=200, beta_exp=2.0)

        # --- Re-identify components by cosine similarity ---
        component_assignment = {}
        used_components = set()
        orig_templates = np.column_stack([templates[name] for name in drum_names])

        for drum_idx, drum_name in enumerate(drum_names):
            best_comp = -1
            best_score = -1
            t_vec = orig_templates[:, drum_idx]
            t_vec = t_vec / (np.linalg.norm(t_vec) + 1e-10)

            for comp_idx in range(n_components):
                if comp_idx in used_components:
                    continue
                c_vec = W[:, comp_idx]
                c_vec = c_vec / (np.linalg.norm(c_vec) + 1e-10)
                score = float(np.dot(t_vec, c_vec))
                if score > best_score:
                    best_score = score
                    best_comp = comp_idx

            component_assignment[drum_name] = best_comp
            used_components.add(best_comp)

        # --- Cross-talk suppression between overlapping drums ---
        activations = {}
        for drum_name in drum_names:
            comp_idx = component_assignment[drum_name]
            act = H[comp_idx, :].copy()
            # Light smoothing
            kernel = np.ones(3) / 3.0
            act = np.convolve(act, kernel, mode='same')
            activations[drum_name] = act

        activations = _suppress_cross_talk(activations, drum_names)

        # --- Peak-pick with onset gating ---
        midi_map = {
            'kick': 36, 'snare': 38, 'hihat': 42,
            'tom': 48, 'floor_tom': 43, 'crash': 49
        }

        # Per-drum threshold and minimum distance
        drum_params = {
            'kick':      {'thresh': 0.30, 'min_ms': 80,  'gate_r': 3},
            'snare':     {'thresh': 0.30, 'min_ms': 80,  'gate_r': 3},
            'hihat':     {'thresh': 0.25, 'min_ms': 50,  'gate_r': 2},
            'tom':       {'thresh': 0.40, 'min_ms': 100, 'gate_r': 4},
            'floor_tom': {'thresh': 0.40, 'min_ms': 100, 'gate_r': 4},
            'crash':     {'thresh': 0.35, 'min_ms': 150, 'gate_r': 4},
        }

        seconds_per_beat = 60.0 / tempo
        seconds_per_32nd = seconds_per_beat / 8.0
        frame_times = librosa.frames_to_time(
            np.arange(H.shape[1]), sr=sr, hop_length=hop_length
        )

        notes = []

        for drum_name in drum_names:
            params = drum_params[drum_name]
            activation = activations[drum_name]
            min_dist = max(3, int(params['min_ms'] / 1000.0 * sr / hop_length))

            peaks = _peak_pick_with_onset_gate(
                activation,
                onset_frames_set,
                onset_env,
                threshold_ratio=params['thresh'],
                min_distance_frames=min_dist,
                gate_radius=params['gate_r']
            )

            for peak_frame in peaks:
                onset_time = float(frame_times[peak_frame])
                vel = float(activation[peak_frame])
                p95 = float(np.percentile(activation, 95)) + 1e-10
                vel_norm = min(1.0, vel / p95)
                vel_norm = max(0.1, vel_norm)

                beat_position = onset_time / seconds_per_beat
                bar = int(beat_position / 4) + 1
                beat_in_bar = int(beat_position % 4) + 1
                sub_beat = int((beat_position % 1) * 8) + 1
                quantized_time = round(onset_time / seconds_per_32nd) * seconds_per_32nd

                notes.append({
                    'time_seconds': round(quantized_time, 6),
                    'midi_note': midi_map[drum_name],
                    'velocity': round(vel_norm, 3),
                    'bar': bar,
                    'beat': beat_in_bar,
                    'sub_beat': sub_beat,
                    'drum_type': drum_name
                })

        # --- Post-processing ---
        notes = _apply_musical_rules(notes, tempo)
        notes = _enforce_max_simultaneous(notes, max_simultaneous=4)
        notes.sort(key=lambda n: n['time_seconds'])

        transcription = {
            'bpm': round(tempo, 2),
            'time_signature': {'numerator': 4, 'denominator': 4},
            'duration_seconds': round(duration, 3),
            'notes': notes
        }

        with open(output_path, 'w') as f:
            json.dump(transcription, f, indent=2)

        from collections import Counter
        dist = Counter(n['drum_type'] for n in notes)
        print(f'Transcription complete: {len(notes)} notes at {tempo:.1f} BPM')
        print(f'Distribution: {dict(dist)}')
        return True
    except Exception as e:
        import traceback
        traceback.print_exc()
        print(f'Error during transcription: {str(e)}', file=sys.stderr)
        return False


if __name__ == '__main__':
    if len(sys.argv) < 3:
        print('Usage: python script.py <audio_path> <output_path>', file=sys.stderr)
        sys.exit(1)
    success = transcribe_drums(sys.argv[1], sys.argv[2])
    sys.exit(0 if success else 1)

";

    /// <summary>
    /// Creates a basic fallback transcription using NAudio when Python/librosa is not available.
    /// This is a simpler energy-based onset detection.
    /// </summary>
    private static async Task CreateFallbackTranscriptionAsync(string drumStemPath, string outputJsonPath)
    {
        // Create a minimal valid transcription
        var transcription = new TranscriptionData
        {
            BPM = 120,
            TimeSignature = new TimeSignatureInfo { Numerator = 4, Denominator = 4 },
            DurationSeconds = 0,
            Notes = new List<TranscriptionNote>()
        };

        try
        {
            using var reader = new NAudio.Wave.AudioFileReader(drumStemPath);
            transcription.DurationSeconds = reader.TotalTime.TotalSeconds;

            // Simple energy-based onset detection
            var sampleRate = reader.WaveFormat.SampleRate;
            var channels = reader.WaveFormat.Channels;
            var hopSize = sampleRate / 100; // 10ms hops
            var buffer = new float[hopSize * channels];

            double estimatedBpm = 120;
            var secondsPerBeat = 60.0 / estimatedBpm;
            var secondsPer32nd = secondsPerBeat / 8.0;

            double prevEnergy = 0;
            double time = 0;
            double threshold = 0.01;
            double minInterval = 0.03; // minimum 30ms between onsets
            double lastOnsetTime = -1;

            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                // Calculate energy
                double energy = 0;
                for (int i = 0; i < read; i++)
                    energy += buffer[i] * buffer[i];
                energy /= read;

                // Onset = significant energy increase
                if (energy > threshold && energy > prevEnergy * 3 && (time - lastOnsetTime) > minInterval)
                {
                    var quantizedTime = Math.Round(time / secondsPer32nd) * secondsPer32nd;
                    var beatPos = quantizedTime / secondsPerBeat;

                    transcription.Notes.Add(new TranscriptionNote
                    {
                        TimeSeconds = Math.Round(quantizedTime, 6),
                        MidiNote = 38, // Default to snare
                        Velocity = Math.Min(1.0, Math.Sqrt(energy) * 20),
                        Bar = (int)(beatPos / 4) + 1,
                        Beat = (int)(beatPos % 4) + 1,
                        SubBeat = (int)((beatPos % 1) * 8) + 1,
                        DrumType = "snare"
                    });

                    lastOnsetTime = time;
                }

                prevEnergy = energy;
                time += (double)read / channels / sampleRate;
            }

            transcription.BPM = estimatedBpm;
        }
        catch
        {
            // If even fallback fails, write empty transcription
        }

        var json = JsonSerializer.Serialize(transcription, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputJsonPath, json);
    }

    /// <summary>
    /// Converts a FLAC file to WAV using Python/librosa
    /// </summary>
    private async Task<bool> ConvertFlacToWavAsync(string flacPath, string wavPath, CancellationToken cancellationToken)
    {
        var conversionScript = @"
import librosa
import soundfile as sf
import sys

try:
    y, sr = librosa.load(sys.argv[1], sr=44100, mono=True)
    sf.write(sys.argv[2], y, sr)
    print('Conversion complete')
except Exception as e:
    print(f'Conversion failed: {e}', file=sys.stderr)
    sys.exit(1)
";
        var scriptPath = Path.Combine(Path.GetTempPath(), "convert_flac_to_wav.py");
        await File.WriteAllTextAsync(scriptPath, conversionScript, cancellationToken);

        var args = $"\"{scriptPath}\" \"{flacPath}\" \"{wavPath}\"";
        var processInfo = new ProcessStartInfo
        {
            FileName = _pythonPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return false;

            await process.WaitForExitAsync(cancellationToken);
            var success = process.ExitCode == 0 && File.Exists(wavPath);

            try { File.Delete(scriptPath); } catch { }

            return success;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Fallback separation using librosa HPSS (harmonic/percussive split).
    /// Writes percussive component as drum stem and harmonic component as drumless backing.
    /// </summary>
    private async Task<bool> CreateFallbackStemSeparationAsync(
        string inputAudioPath,
        string drumStemOutputPath,
        string drumlessOutputPath,
        CancellationToken cancellationToken)
    {
        var script = @"
import librosa
import soundfile as sf
import numpy as np
import sys

def main(inp, drums_out, drumless_out):
    y, sr = librosa.load(inp, sr=44100, mono=False)

    # Normalize shape so time axis is last.
    if isinstance(y, np.ndarray) and y.ndim == 1:
        y = y[np.newaxis, :]

    harmonic, percussive = librosa.effects.hpss(y)

    # soundfile expects [samples, channels] for multi-channel audio.
    if percussive.ndim > 1:
        sf.write(drums_out, percussive.T, sr)
    else:
        sf.write(drums_out, percussive, sr)

    if harmonic.ndim > 1:
        sf.write(drumless_out, harmonic.T, sr)
    else:
        sf.write(drumless_out, harmonic, sr)

if __name__ == '__main__':
    if len(sys.argv) < 4:
        sys.exit(1)
    try:
        main(sys.argv[1], sys.argv[2], sys.argv[3])
        sys.exit(0)
    except Exception as e:
        print(str(e), file=sys.stderr)
        sys.exit(1)
";

        var scriptPath = Path.Combine(Path.GetTempPath(), "fallback_stem_split.py");
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = _pythonPath,
                Arguments = $"\"{scriptPath}\" \"{inputAudioPath}\" \"{drumStemOutputPath}\" \"{drumlessOutputPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null) return false;

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 && File.Exists(drumStemOutputPath) && File.Exists(drumlessOutputPath);
        }
        catch
        {
            return false;
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }
}

