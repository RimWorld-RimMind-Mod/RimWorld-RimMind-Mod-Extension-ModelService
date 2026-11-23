using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces.Audio;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Audio;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Client;
using RimMind.ModelService.Security;
using CoreHttpErrorClassifier = RimMind.Application.Features.Llm.HttpErrorClassifier;

namespace RimMind.ModelService.Audio
{
    /// <summary>
    /// DeepSeek official speech-to-text (<c>/v1/tools/asr</c>) adapter for <see cref="ISpeechRecognizer"/>.
    /// </summary>
    public sealed class DeepSeekSpeechRecognizer : ISpeechRecognizer
    {
        public const int MaxAudioBytes = 8 * 1024 * 1024;
        private const int RequestTimeoutSeconds = 60;

        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _httpSend;
        private readonly Func<(string rootBaseUrl, string apiKey)?> _credentialResolver;

        public DeepSeekSpeechRecognizer()
            : this(null, null)
        {
        }

        internal DeepSeekSpeechRecognizer(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? httpSendOverride,
            Func<(string rootBaseUrl, string apiKey)?>? credentialResolver = null)
        {
            _httpSend = httpSendOverride ?? ((req, ct) => RimMindHttpClientPool.NoRedirect.SendAsync(req, ct));
            _credentialResolver = credentialResolver ?? DeepSeekAudioEndpointResolver.Resolve;
        }

        public string Id => "deepseek_asr";
        public string OwnerModId => ModelServiceClientFactory.ModelServiceOwnerModId;
        public string DisplayLabel => "DeepSeek ASR";

        public bool IsConfigured() => _credentialResolver() != null;

        public async Task<Result<SpeechRecognitionResult, RimMindError>> TranscribeAsync(SpeechRecognitionRequest request)
        {
            if (request == null)
                return Result<SpeechRecognitionResult, RimMindError>.Err(
                    RimMindErrors.ClientPermanent("SpeechRecognitionRequest cannot be null."));

            if (request.Ct.IsCancellationRequested)
                return Result<SpeechRecognitionResult, RimMindError>.Err(RimMindErrors.Cancelled());

            var resolved = _credentialResolver();
            if (resolved == null)
                return Result<SpeechRecognitionResult, RimMindError>.Err(
                    RimMindErrors.ClientNotConfigured("No active DeepSeek endpoint or API key configured for ASR."));

            if (request.AudioBytes == null || request.AudioBytes.Length == 0)
                return Result<SpeechRecognitionResult, RimMindError>.Err(
                    RimMindErrors.ClientPermanent("ASR audio payload cannot be empty."));

            if (request.AudioBytes.Length > MaxAudioBytes)
                return Result<SpeechRecognitionResult, RimMindError>.Err(
                    RimMindErrors.ClientPermanent($"ASR audio payload exceeds DeepSeek {MaxAudioBytes} byte limit ({request.AudioBytes.Length} bytes)."));

            string rootBaseUrl = DeepSeekAudioEndpointResolver.NormalizeRootBaseUrl(resolved.Value.rootBaseUrl);
            string apiKey = resolved.Value.apiKey;
            string url = rootBaseUrl + "/v1/tools/asr";
            string fileName = !string.IsNullOrWhiteSpace(request.FileName)
                ? request.FileName.Trim()
                : DefaultFileName(request.Format);

            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(RequestTimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(request.Ct, timeoutCts.Token);
                using var multipart = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(request.AudioBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(ResolveMimeType(request.Format));
                multipart.Add(fileContent, "file", fileName);

                using var httpReq = new HttpRequestMessage(HttpMethod.Post, url);
                httpReq.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
                httpReq.Content = multipart;

                using var response = await _httpSend(httpReq, linkedCts.Token).ConfigureAwait(false);
                string body = response.Content != null
                    ? await response.Content.ReadAsStringAsync().ConfigureAwait(false)
                    : string.Empty;

                if (!response.IsSuccessStatusCode)
                {
                    string safeBody = SecretRedactor.ForMessageWithSecret(body, apiKey, 512);
                    return Result<SpeechRecognitionResult, RimMindError>.Err(
                        CoreHttpErrorClassifier.Classify((int)response.StatusCode, safeBody, DisplayLabel));
                }

                return ParseSuccessResponse(body, apiKey);
            }
            catch (OperationCanceledException) when (request.Ct.IsCancellationRequested)
            {
                return Result<SpeechRecognitionResult, RimMindError>.Err(RimMindErrors.Cancelled());
            }
            catch (OperationCanceledException)
            {
                return Result<SpeechRecognitionResult, RimMindError>.Err(
                    RimMindErrors.Timeout($"DeepSeek ASR request timed out after {RequestTimeoutSeconds}s."));
            }
            catch (Exception ex)
            {
                return Result<SpeechRecognitionResult, RimMindError>.Err(
                    RimMindErrors.ClientTransient(
                        $"DeepSeek ASR request failed: {SecretRedactor.ForMessageWithSecret(ex.Message, apiKey, 512)}",
                        ex));
            }
        }

        private Result<SpeechRecognitionResult, RimMindError> ParseSuccessResponse(string body, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(body))
                return Result<SpeechRecognitionResult, RimMindError>.Err(
                    RimMindErrors.ClientTransient("DeepSeek ASR returned an empty response body."));

            try
            {
                var token = JToken.Parse(body.Trim());
                if (token["error"] != null && token["error"]!.Type != JTokenType.Null)
                {
                    string errMsg = token["error"]?["message"]?.ToString() ?? "Unknown DeepSeek ASR error";
                    return Result<SpeechRecognitionResult, RimMindError>.Err(
                        RimMindErrors.ClientPermanent(SecretRedactor.ForMessageWithSecret(errMsg, apiKey, 512)));
                }

                string text = token["text"]?.ToString() ?? string.Empty;
                int durationMs = token["duration_ms"]?.Value<int>() ?? 0;

                if (token["segments"] is JArray segments && segments.Count > 0)
                {
                    if (string.IsNullOrEmpty(text))
                    {
                        var parts = new List<string>(segments.Count);
                        for (int i = 0; i < segments.Count; i++)
                        {
                            string? segText = segments[i]?["text"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(segText))
                                parts.Add(segText!.Trim());
                        }
                        text = string.Join(" ", parts);
                    }

                    if (durationMs <= 0)
                    {
                        var lastSeg = segments[segments.Count - 1];
                        durationMs = lastSeg?["end_ms"]?.Value<int>() ?? 0;
                    }
                }

                return Result<SpeechRecognitionResult, RimMindError>.Ok(new SpeechRecognitionResult
                {
                    RequestId = token["request_id"]?.ToString() ?? Guid.NewGuid().ToString("N"),
                    Text = text,
                    DurationMs = Math.Max(0, durationMs),
                    ProviderId = Id
                });
            }
            catch (Exception ex)
            {
                return Result<SpeechRecognitionResult, RimMindError>.Err(
                    RimMindErrors.ClientPermanent(
                        $"Failed to parse DeepSeek ASR response: {SecretRedactor.ForMessageWithSecret(ex.Message, apiKey, 512)}"));
            }
        }

        private static string ResolveMimeType(AudioContainerFormat format) => format switch
        {
            AudioContainerFormat.Mp3 => "audio/mpeg",
            AudioContainerFormat.OggOpus => "audio/ogg",
            AudioContainerFormat.Pcm16Mono => "audio/L16",
            _ => "audio/wav"
        };

        private static string DefaultFileName(AudioContainerFormat format) => format switch
        {
            AudioContainerFormat.Mp3 => "audio.mp3",
            AudioContainerFormat.OggOpus => "audio.ogg",
            AudioContainerFormat.Pcm16Mono => "audio.pcm",
            _ => "audio.wav"
        };
    }
}
