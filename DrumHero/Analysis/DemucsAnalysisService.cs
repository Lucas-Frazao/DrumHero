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
    print('librosa not installed, using basic onset detection', file=sys.stderr)
    sys.exit(1)

def transcribe_drums(audio_path, output_path):
    try:
        # Load audio
        y, sr = librosa.load(audio_path, sr=44100, mono=True)
        duration = librosa.get_duration(y=y, sr=sr)
        
        # Estimate tempo
        tempo, beat_frames = librosa.beat.beat_track(y=y, sr=sr)
        if hasattr(tempo, '__len__'):
            tempo = float(tempo[0])
        else:
            tempo = float(tempo)
        
        # Onset detection
        onset_env = librosa.onset.onset_strength(y=y, sr=sr)
        onset_frames = librosa.onset.onset_detect(y=y, sr=sr, onset_envelope=onset_env)
        onset_times = librosa.frames_to_time(onset_frames, sr=sr)
        
        # Frequency analysis for each onset to classify drum type
        notes = []
        seconds_per_beat = 60.0 / tempo
        seconds_per_32nd = seconds_per_beat / 8.0
        
        for onset_time in onset_times:
            # Get a short window around the onset
            start_sample = int(onset_time * sr)
            end_sample = min(start_sample + int(0.05 * sr), len(y))
            if start_sample >= len(y):
                continue
            
            segment = y[start_sample:end_sample]
            if len(segment) < 256:
                continue
            
            # Spectral centroid to classify
            centroid = librosa.feature.spectral_centroid(y=segment, sr=sr)
            mean_centroid = float(np.mean(centroid))
            
            # RMS for velocity
            rms = float(np.sqrt(np.mean(segment**2)))
            velocity = min(1.0, rms * 10.0)
            
            # Classify based on spectral centroid
            if mean_centroid < 500:
                midi_note = 36  # Kick
                drum_type = 'kick'
            elif mean_centroid < 2000:
                midi_note = 38  # Snare
                drum_type = 'snare'
            elif mean_centroid < 4000:
                midi_note = 42  # Hi-hat closed
                drum_type = 'hihat_closed'
            elif mean_centroid < 6000:
                midi_note = 48  # Tom
                drum_type = 'tom1'
            else:
                midi_note = 49  # Crash
                drum_type = 'crash1'
            
            # Calculate bar/beat position
            beat_position = onset_time / seconds_per_beat
            bar = int(beat_position / 4) + 1
            beat_in_bar = int(beat_position % 4) + 1
            sub_beat = int((beat_position % 1) * 8) + 1
            
            # Quantize to nearest 1/32 note
            quantized_time = round(onset_time / seconds_per_32nd) * seconds_per_32nd
            
            notes.append({
                'time_seconds': round(quantized_time, 6),
                'midi_note': midi_note,
                'velocity': round(velocity, 3),
                'bar': bar,
                'beat': beat_in_bar,
                'sub_beat': sub_beat,
                'drum_type': drum_type
            })
        
        transcription = {
            'bpm': round(tempo, 2),
            'time_signature': {'numerator': 4, 'denominator': 4},
            'duration_seconds': round(duration, 3),
            'notes': notes
        }
        
        with open(output_path, 'w') as f:
            json.dump(transcription, f, indent=2)
        
        print(f'Transcription complete: {len(notes)} notes at {tempo:.1f} BPM')
        return True
    except Exception as e:
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

