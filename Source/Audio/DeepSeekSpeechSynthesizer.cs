using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
    /// DeepSeek official text-to-speech (<c>/v1/tools/tts</c>) adapter for <see cref="ISpeechSynthesizer"/>.
    /// </summary>
    public sealed class DeepSeekSpeechSynthesizer : ISpeechSynthesizer
    {
        public const int MaxBillingCharacters = 10000;
        public const int MaxPayloadBytes = 64 * 1024;
        public const string DefaultVoiceId = "stella";
        private const int RequestTimeoutSeconds = 45;

        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _httpSend;
        private readonly Func<(string rootBaseUrl, string apiKey)?> _credentialResolver;

        public DeepSeekSpeechSynthesizer()
            : this(null, null)
        {
        }

        internal DeepSeekSpeechSynthesizer(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? httpSendOverride,
            Func<(string rootBaseUrl, string apiKey)?>? credentialResolver = null)
        {
            _httpSend = httpSendOverride ?? ((req, ct) => RimMindHttpClientPool.Shared.SendAsync(req, ct));
            _credentialResolver = credentialResolver ?? DeepSeekAudioEndpointResolver.Resolve;
        }

        public string Id => "deepseek_tts";
        public string OwnerModId => ModelServiceClientFactory.ModelServiceOwnerModId;
        public string DisplayLabel => "DeepSeek TTS";

        public bool IsConfigured() => _credentialResolver() != null;

        public static int ComputeBillingCharacters(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int count = 0;
            for (int i = 0; i < text!.Length; i++)
            {
                char ch = text[i];
                count += (ch >= '\u4E00' && ch <= '\u9FFF') ? 2 : 1;
            }
            return count;
        }

        public async Task<Result<SpeechSynthesisResult, RimMindError>> SynthesizeAsync(SpeechSynthesisRequest request)
        {
            if (request == null)
                return Result<SpeechSynthesisResult, RimMindError>.Err(
                    RimMindErrors.ClientPermanent("SpeechSynthesisRequest cannot be null."));

            if (request.Ct.IsCancellationRequested)
                return Result<SpeechSynthesisResult, RimMindError>.Err(RimMindErrors.Cancelled());

            var resolved = _credentialResolver();
            if (resolved == null)
                return Result<SpeechSynthesisResult, RimMindError>.Err(
                    RimMindErrors.ClientNotConfigured("No active DeepSeek endpoint or API key configured for TTS."));

            string trimmedText = (request.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedText))
                return Result<SpeechSynthesisResult, RimMindError>.Err(
                    RimMindErrors.ClientPermanent("TTS input text cannot be empty."));

            int billingChars = ComputeBillingCharacters(trimmedText);
            if (billingChars > MaxBillingCharacters)
                return Result<SpeechSynthesisResult, RimMindError>.Err(
                    RimMindErrors.ClientPermanent($"TTS text exceeds DeepSeek {MaxBillingCharacters} billing-character limit ({billingChars} billing chars)."));

            (string wireFormat, AudioContainerFormat outputFormat) = ResolveWireFormat(request.PreferredFormat);
            string voiceId = ResolveVoiceId(request.VoiceId);

            var payload = new JObject
            {
                ["text"] = trimmedText,
                ["voice_id"] = voiceId,
                ["format"] = wireFormat,
                ["filter_markdown"] = request.StripMarkdown
            };

            string jsonBody = payload.ToString(Formatting.None);
            int bodyBytes = Encoding.UTF8.GetByteCount(jsonBody);
            if (bodyBytes > MaxPayloadBytes)
                return Result<SpeechSynthesisResult, RimMindError>.Err(
                    RimMindErrors.ClientPermanent($"TTS request body exceeds DeepSeek {MaxPayloadBytes} byte limit ({bodyBytes} bytes)."));

            string rootBaseUrl = DeepSeekAudioEndpointResolver.NormalizeRootBaseUrl(resolved.Value.rootBaseUrl);
            string apiKey = resolved.Value.apiKey;
            string url = rootBaseUrl + "/v1/tools/tts";

            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(RequestTimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(request.Ct, timeoutCts.Token);
                using var httpReq = new HttpRequestMessage(HttpMethod.Post, url);
                httpReq.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
                httpReq.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                using var response = await _httpSend(httpReq, linkedCts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    string errBody = response.Content != null
                        ? await response.Content.ReadAsStringAsync().ConfigureAwait(false)
                        : string.Empty;
                    string safeBody = SecretRedactor.ForMessageWithSecret(errBody, apiKey, 512);
                    return Result<SpeechSynthesisResult, RimMindError>.Err(
                        CoreHttpErrorClassifier.Classify((int)response.StatusCode, safeBody, DisplayLabel));
                }

                byte[] audioBytes = response.Content != null
                    ? await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)
                    : Array.Empty<byte>();
                if (audioBytes.Length == 0)
                    return Result<SpeechSynthesisResult, RimMindError>.Err(
                        RimMindErrors.ClientTransient("DeepSeek TTS returned an empty audio stream."));

                return Result<SpeechSynthesisResult, RimMindError>.Ok(new SpeechSynthesisResult
                {
                    AudioBytes = audioBytes,
                    Format = outputFormat,
                    SampleRateHz = 24000,
                    Channels = 1,
                    BitsPerSample = 16,
                    ProviderId = Id
                });
            }
            catch (OperationCanceledException) when (request.Ct.IsCancellationRequested)
            {
                return Result<SpeechSynthesisResult, RimMindError>.Err(RimMindErrors.Cancelled());
            }
            catch (OperationCanceledException)
            {
                return Result<SpeechSynthesisResult, RimMindError>.Err(
                    RimMindErrors.Timeout($"DeepSeek TTS request timed out after {RequestTimeoutSeconds}s."));
            }
            catch (Exception ex)
            {
                return Result<SpeechSynthesisResult, RimMindError>.Err(
                    RimMindErrors.ClientTransient(
                        $"DeepSeek TTS request failed: {SecretRedactor.ForMessageWithSecret(ex.Message, apiKey, 512)}",
                        ex));
            }
        }

        internal static string ResolveVoiceId(string? voiceId)
        {
            if (string.IsNullOrWhiteSpace(voiceId))
                return DefaultVoiceId;

            string trimmed = voiceId!.Trim();
            if (string.Equals(trimmed, "female", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "female-calm", StringComparison.OrdinalIgnoreCase))
            {
                return "stella";
            }

            if (string.Equals(trimmed, "male", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "male-deep", StringComparison.OrdinalIgnoreCase))
            {
                return "tide";
            }

            return trimmed;
        }

        private static (string wireFormat, AudioContainerFormat actualFormat) ResolveWireFormat(AudioContainerFormat preferred) =>
            preferred == AudioContainerFormat.Pcm16Mono
                ? ("pcm", AudioContainerFormat.Pcm16Mono)
                : ("wav", AudioContainerFormat.Wav);
    }
}
