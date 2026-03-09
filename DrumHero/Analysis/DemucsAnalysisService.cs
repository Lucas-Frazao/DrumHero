using DrumHero.Models;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DrumHero.Analysis;

/// <summary>
/// Analysis service that uses Demucs for stem separation (FLAC → drumless backing track)
/// and MidiTranscriptionService for drum transcription (Songsterr MIDI → highway notes).
/// 
/// Pipeline:
/// 1. Convert FLAC to WAV if needed
/// 2. Run Demucs to separate drums from the rest of the track
/// 3. Parse the Songsterr MIDI file to generate the drum highway transcription
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
    private readonly MidiTranscriptionService _midiTranscription;

    public DemucsAnalysisService(string pythonPath = "python", string modelName = "htdemucs")
    {
        _pythonPath = pythonPath;
        _modelName = modelName;
        _midiTranscription = new MidiTranscriptionService();
    }

    public async Task<AnalysisResult> AnalyzeAsync(
        string flacFilePath,
        string midiFilePath,
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

            // Get audio duration for transcription metadata
            double audioDuration = 0;
            try
            {
                using var reader = new NAudio.Wave.AudioFileReader(audioPath);
                audioDuration = reader.TotalTime.TotalSeconds;
            }
            catch { /* Duration will be inferred from MIDI if audio read fails */ }

            // Stage 1: Run Demucs separation (audio only — creates drumless backing track)
            progress?.Report(new AnalysisProgress
            {
                Stage = "Separating",
                ProgressFraction = 0.1,
                Message = "Running Demucs stem separation..."
            });

            var demucsOutputDir = Path.Combine(outputDirectory, "demucs_output");
            Directory.CreateDirectory(demucsOutputDir);

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

            string finalDrumlessPath;
            string finalDrumsPath;

            if (process.ExitCode != 0)
            {
                var demucsDetails = BuildProcessDetails();

                // Demucs failed. Try a librosa HPSS fallback.
                progress?.Report(new AnalysisProgress
                {
                    Stage = "Fallback",
                    ProgressFraction = 0.65,
                    Message = "Demucs failed, trying fallback stem separation..."
                });

                finalDrumsPath = Path.Combine(outputDirectory, "drum_stem.wav");
                finalDrumlessPath = Path.Combine(outputDirectory, "drumless_track.wav");
                var separated = await CreateFallbackStemSeparationAsync(
                    audioPath, finalDrumsPath, finalDrumlessPath, cancellationToken);

                if (!separated)
                {
                    result.Success = false;
                    result.ErrorMessage =
                        "Demucs separation failed and fallback separation also failed.\n\n" + demucsDetails;
                    return result;
                }
            }
            else
            {
                // Demucs succeeded — locate output files
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

                finalDrumsPath = Path.Combine(outputDirectory, "drum_stem.wav");
                finalDrumlessPath = Path.Combine(outputDirectory, "drumless_track.wav");
                File.Copy(drumsPath, finalDrumsPath, overwrite: true);
                File.Copy(noDrumsPath, finalDrumlessPath, overwrite: true);
            }

            progress?.Report(new AnalysisProgress
            {
                Stage = "Separating",
                ProgressFraction = 0.7,
                Message = "Stem separation complete."
            });

            // Stage 2: Generate transcription from Songsterr MIDI file
            var transcriptionPath = Path.Combine(outputDirectory, "transcription.json");
            await _midiTranscription.GenerateTranscriptionAsync(
                midiFilePath, transcriptionPath, audioDuration, progress, cancellationToken);

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
    /// Converts a FLAC file to WAV using Python/librosa.
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

    if isinstance(y, np.ndarray) and y.ndim == 1:
        y = y[np.newaxis, :]

    harmonic, percussive = librosa.effects.hpss(y)

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
