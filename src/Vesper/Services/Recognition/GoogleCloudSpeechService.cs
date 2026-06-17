using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Speech.V2;
using Google.Protobuf;

namespace Vesper.Services.Recognition;

public sealed class GoogleCloudSpeechService : ISpeechRecognitionService
{
    private SpeechClient? _client;
    private string _projectId = string.Empty;
    private string _credentialsPath = string.Empty;
    private string _model = "chirp_2";
    private string _location = "us-central1";

    // Streaming state
    private SpeechClient.StreamingRecognizeStream? _activeStream;
    private Channel<byte[]>? _audioChannel;
    private Task? _audioSenderTask;
    private Task? _responseReaderTask;
    private CancellationTokenSource? _streamingCts;

    public event EventHandler<string>? StreamingResultReceived;

    private string RecognizerName => $"projects/{_projectId}/locations/{_location}/recognizers/_";

    public bool IsAvailable => _client != null && !string.IsNullOrEmpty(_projectId);
    public bool IsStreaming => _activeStream != null;

    public void Configure(string credentialsPath, string? projectId = null, string model = "chirp_2")
    {
        if (string.IsNullOrWhiteSpace(credentialsPath))
            throw new ArgumentException("Google credentials path is empty.", nameof(credentialsPath));

        if (!File.Exists(credentialsPath))
            throw new FileNotFoundException("Google credentials file not found.", credentialsPath);

        _credentialsPath = credentialsPath;
        _model = string.IsNullOrWhiteSpace(model) ? "chirp_2" : model;

        // Chirp models require us-central1; standard models use global
        _location = _model.StartsWith("chirp") ? "us-central1" : "global";

        if (string.IsNullOrWhiteSpace(projectId))
            _projectId = ExtractProjectIdFromCredentials(credentialsPath);
        else
            _projectId = projectId;

        if (string.IsNullOrEmpty(_projectId))
        {
            DiagnosticLogger.Log("GoogleCloud: no project_id found — provide it manually or check credentials file.");
            return;
        }

        ValidateCredentialsFile(credentialsPath);

        var credential = CredentialFactory
            .FromFile<ServiceAccountCredential>(credentialsPath)
            .ToGoogleCredential()
            .CreateScoped("https://www.googleapis.com/auth/cloud-platform");

        _client = new SpeechClientBuilder
        {
            GoogleCredential = credential,
            Endpoint = _location == "global" ? null : $"{_location}-speech.googleapis.com:443"
        }.Build();

        DiagnosticLogger.Log($"GoogleCloud: configured, project={_projectId}, model={_model}, location={_location}");
    }

    // ── Batch Recognition ──────────────────────────────────────────

    public async Task<string> TranscribeAsync(byte[] wavAudio, string language, CancellationToken ct = default)
    {
        if (_client == null)
            throw new InvalidOperationException("Google Cloud Speech not configured. Set credentials path in Settings.");

        var config = new RecognitionConfig
        {
            AutoDecodingConfig = new AutoDetectDecodingConfig(),
            Model = _model
        };

        config.LanguageCodes.AddRange(GetLanguageCodes(language));

        var request = new RecognizeRequest
        {
            Recognizer = RecognizerName,
            Config = config,
            Content = ByteString.CopyFrom(wavAudio)
        };

        try
        {
            var response = await _client.RecognizeAsync(request, ct);

            return string.Join(" ", response.Results
                .SelectMany(r => r.Alternatives)
                .Select(a => a.Transcript))
                .Trim();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"GoogleCloud TranscribeAsync error: {ex.Message}");
            DiagnosticLogger.Log($"  Recognizer: {RecognizerName}, Model: {_model}, Langs: {string.Join(",", GetLanguageCodes(language))}");
            throw;
        }
    }

    // ── Streaming Recognition ──────────────────────────────────────

    public async Task StartStreamingAsync(string language, CancellationToken ct)
    {
        if (_client == null)
            throw new InvalidOperationException("Google Cloud Speech not configured.");

        await StopStreamingAsync();

        _streamingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activeStream = _client.StreamingRecognize();
        _audioChannel = Channel.CreateUnbounded<byte[]>();

        var streamingRecogConfig = new RecognitionConfig
        {
            ExplicitDecodingConfig = new ExplicitDecodingConfig
            {
                Encoding = ExplicitDecodingConfig.Types.AudioEncoding.Linear16,
                SampleRateHertz = 16000,
                AudioChannelCount = 1
            },
            Model = _model
        };
        streamingRecogConfig.LanguageCodes.AddRange(GetLanguageCodes(language));

        await _activeStream.WriteAsync(new StreamingRecognizeRequest
        {
            Recognizer = RecognizerName,
            StreamingConfig = new StreamingRecognitionConfig
            {
                Config = streamingRecogConfig,
                StreamingFeatures = new StreamingRecognitionFeatures
                {
                    InterimResults = true
                }
            }
        });

        _audioSenderTask = Task.Run(() => AudioSenderLoopAsync(_streamingCts.Token));
        _responseReaderTask = Task.Run(() => ResponseReaderLoopAsync(_streamingCts.Token));

        DiagnosticLogger.Log("GoogleCloud: streaming session started");
    }

    public void FeedAudioSamples(float[] samples)
    {
        if (_audioChannel == null) return;

        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            var val = (short)(Math.Clamp(samples[i], -1f, 1f) * 32767);
            pcm[i * 2] = (byte)(val & 0xFF);
            pcm[i * 2 + 1] = (byte)((val >> 8) & 0xFF);
        }

        _audioChannel.Writer.TryWrite(pcm);
    }

    public async Task StopStreamingAsync()
    {
        if (_activeStream == null) return;

        try
        {
            _audioChannel?.Writer.TryComplete();

            if (_audioSenderTask != null)
                await _audioSenderTask;

            await _activeStream.WriteCompleteAsync();

            if (_responseReaderTask != null)
                await _responseReaderTask;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"GoogleCloud: streaming stop error: {ex.Message}");
        }
        finally
        {
            _streamingCts?.Cancel();
            _streamingCts?.Dispose();
            _streamingCts = null;
            _activeStream = null;
            _audioChannel = null;
            _audioSenderTask = null;
            _responseReaderTask = null;
        }

        DiagnosticLogger.Log("GoogleCloud: streaming session ended");
    }

    private async Task AudioSenderLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var chunk in _audioChannel!.Reader.ReadAllAsync(ct))
            {
                if (_activeStream == null) break;
                await _activeStream.WriteAsync(new StreamingRecognizeRequest
                {
                    Audio = ByteString.CopyFrom(chunk)
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"GoogleCloud: audio sender error: {ex.Message}");
        }
    }

    private async Task ResponseReaderLoopAsync(CancellationToken ct)
    {
        try
        {
            var responses = _activeStream!.GetResponseStream();
            while (await responses.MoveNextAsync(ct))
            {
                var response = responses.Current;
                foreach (var result in response.Results)
                {
                    var transcript = result.Alternatives.FirstOrDefault()?.Transcript;
                    if (!string.IsNullOrEmpty(transcript) && result.IsFinal)
                    {
                        StreamingResultReceived?.Invoke(this, transcript);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"GoogleCloud: response reader error: {ex.Message}");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static string[] GetLanguageCodes(string? lang)
    {
        // Chirp/Chirp 2 auto-detect language when no codes are specified
        if (string.IsNullOrEmpty(lang) || lang == "auto")
            return [];

        return [MapLanguageCode(lang)];
    }

    private static string MapLanguageCode(string lang)
    {
        return lang switch
        {
            "en" => "en-US",
            "pl" => "pl-PL",
            "de" => "de-DE",
            "fr" => "fr-FR",
            "es" => "es-ES",
            "it" => "it-IT",
            "pt" => "pt-BR",
            "nl" => "nl-NL",
            "ja" => "ja-JP",
            "zh" => "cmn-Hans-CN",
            "ko" => "ko-KR",
            "ru" => "ru-RU",
            "uk" => "uk-UA",
            "cs" => "cs-CZ",
            "sv" => "sv-SE",
            _ => lang
        };
    }

    private static string ExtractProjectIdFromCredentials(string path)
    {
        try
        {
            if (!File.Exists(path)) return string.Empty;
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("project_id", out var prop))
                return prop.GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"GoogleCloud: failed to read project_id from credentials: {ex.Message}");
        }
        return string.Empty;
    }

    private static void ValidateCredentialsFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
            var hasPrivateKey = root.TryGetProperty("private_key", out var keyProp)
                && keyProp.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(keyProp.GetString());
            var hasClientEmail = root.TryGetProperty("client_email", out var emailProp)
                && emailProp.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(emailProp.GetString());

            if (type != "service_account" || !hasPrivateKey || !hasClientEmail)
                throw new InvalidOperationException("Google credentials JSON is not a valid service account key.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Google credentials JSON is malformed: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        StopStreamingAsync().GetAwaiter().GetResult();
    }
}
