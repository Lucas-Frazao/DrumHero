using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DrumHero.Analysis;

/// <summary>
/// HTTP-based analysis service for cloud drum separation APIs.
/// Configurable base URL and API key for services like LALAL.AI, MVSEP, etc.
/// 
/// Expected API contract:
/// POST /analyze - Upload .flac file, returns job ID
/// GET /status/{jobId} - Poll for completion
/// GET /results/{jobId}/drumless - Download drumless track
/// GET /results/{jobId}/drums - Download drum stem  
/// GET /results/{jobId}/transcription - Download transcription JSON
/// </summary>
public class HttpAnalysisService : IAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public HttpAnalysisService(string baseUrl, string apiKey)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
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
            // Stage 1: Upload
            progress?.Report(new AnalysisProgress
            {
                Stage = "Uploading",
                ProgressFraction = 0.05,
                Message = "Uploading audio file..."
            });

            using var fileStream = File.OpenRead(flacFilePath);
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("audio/flac");
            content.Add(streamContent, "file", Path.GetFileName(flacFilePath));

            var uploadResponse = await _httpClient.PostAsync(
                $"{_baseUrl}/analyze", content, cancellationToken);
            uploadResponse.EnsureSuccessStatusCode();

            var uploadJson = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
            var jobDoc = JsonDocument.Parse(uploadJson);
            var jobId = jobDoc.RootElement.GetProperty("job_id").GetString()!;

            // Stage 2: Poll for completion
            progress?.Report(new AnalysisProgress
            {
                Stage = "Processing",
                ProgressFraction = 0.2,
                Message = "Processing... this may take a few minutes."
            });

            var pollInterval = TimeSpan.FromSeconds(3);
            var maxWait = TimeSpan.FromMinutes(10);
            var elapsed = TimeSpan.Zero;

            while (elapsed < maxWait)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(pollInterval, cancellationToken);
                elapsed += pollInterval;

                var statusResponse = await _httpClient.GetAsync(
                    $"{_baseUrl}/status/{jobId}", cancellationToken);
                statusResponse.EnsureSuccessStatusCode();

                var statusJson = await statusResponse.Content.ReadAsStringAsync(cancellationToken);
                var statusDoc = JsonDocument.Parse(statusJson);
                var status = statusDoc.RootElement.GetProperty("status").GetString();

                if (statusDoc.RootElement.TryGetProperty("progress", out var progressProp))
                {
                    var pct = progressProp.GetDouble();
                    progress?.Report(new AnalysisProgress
                    {
                        Stage = "Processing",
                        ProgressFraction = 0.2 + pct * 0.5,
                        Message = $"Processing... {pct * 100:F0}%"
                    });
                }

                if (status == "completed") break;
                if (status == "failed")
                {
                    var errorMsg = statusDoc.RootElement.TryGetProperty("error", out var errProp)
                        ? errProp.GetString() : "Unknown error";
                    result.Success = false;
                    result.ErrorMessage = $"Cloud analysis failed: {errorMsg}";
                    return result;
                }
            }

            // Stage 3: Download results
            progress?.Report(new AnalysisProgress
            {
                Stage = "Downloading",
                ProgressFraction = 0.75,
                Message = "Downloading results..."
            });

            Directory.CreateDirectory(outputDirectory);

            // Download drumless track
            var drumlessPath = Path.Combine(outputDirectory, "drumless_track.wav");
            var drumlessBytes = await _httpClient.GetByteArrayAsync(
                $"{_baseUrl}/results/{jobId}/drumless", cancellationToken);
            await File.WriteAllBytesAsync(drumlessPath, drumlessBytes, cancellationToken);

            // Download drum stem
            var drumsPath = Path.Combine(outputDirectory, "drum_stem.wav");
            var drumsBytes = await _httpClient.GetByteArrayAsync(
                $"{_baseUrl}/results/{jobId}/drums", cancellationToken);
            await File.WriteAllBytesAsync(drumsPath, drumsBytes, cancellationToken);

            // Download transcription
            var transcriptionPath = Path.Combine(outputDirectory, "transcription.json");
            var transcriptionBytes = await _httpClient.GetByteArrayAsync(
                $"{_baseUrl}/results/{jobId}/transcription", cancellationToken);
            await File.WriteAllBytesAsync(transcriptionPath, transcriptionBytes, cancellationToken);

            result.Success = true;
            result.DrumlessTrackPath = drumlessPath;
            result.DrumStemPath = drumsPath;
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
        catch (HttpRequestException ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Cloud API error: {ex.Message}\nEnsure the API URL and key are configured correctly.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Analysis failed: {ex.Message}";
        }

        return result;
    }
}
