using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RimMind.ModelService.Models;
using RimMind.ModelService.Protocol;
using RimMind.ModelService.Security;

namespace RimMind.ModelService.Diagnostics
{
    public static class EndpointHealthProbe
    {
        private static readonly HttpClient HttpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        public static Task<(bool success, int latencyMs, string message)> ProbeEndpointAsync(
            ModelEndpointConfig endpoint,
            CancellationToken ct = default)
            => ProbeEndpointAsync(endpoint, ct, null);

        internal static async Task<(bool success, int latencyMs, string message)> ProbeEndpointAsync(
            ModelEndpointConfig endpoint,
            CancellationToken ct,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? httpSendOverride)
        {
            if (endpoint == null || string.IsNullOrWhiteSpace(endpoint.endpoint))
            {
                return (false, -1, "Invalid or empty endpoint URL");
            }

            var protocol = ModelProtocolRegistry.Resolve(endpoint);

            // Security check for local subscription gateway
            if (protocol.LegacyProviderType == ProviderType.LocalSubscriptionGateway)
            {
                if (!LocalLoopbackValidator.IsLoopbackAddress(endpoint.endpoint))
                {
                    return (false, -1, "Security violation: Local subscription gateways must use loopback address (127.0.0.1)");
                }
            }

            var sendAsync = httpSendOverride
                ?? ((req, token) => HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token));

            var stopwatch = Stopwatch.StartNew();
            try
            {
                string resolvedUrl = endpoint.endpoint.TrimEnd('/') + "/models";
                using var request = protocol.BuildHealthProbeRequest(endpoint, resolvedUrl);
                using var response = await sendAsync(request, ct).ConfigureAwait(false);
                stopwatch.Stop();
                int latency = (int)stopwatch.ElapsedMilliseconds;

                int statusCode = (int)response.StatusCode;

                // 401/403 means network-reachable, but credentials are rejected — do NOT mark healthy or reset failures.
                if (statusCode == 401 || statusCode == 403)
                {
                    endpoint.lastLatencyMs = latency;
                    endpoint.isHealthy = false;
                    return (false, latency, $"HTTP {statusCode} reachable but unauthorized");
                }

                // 2xx or 404 (some gateways do not expose /models) means reachable and not auth-rejected
                if (response.IsSuccessStatusCode || statusCode == 404)
                {
                    endpoint.RecordSuccess(latency);
                    endpoint.isHealthy = true;
                    return (true, latency, $"HTTP {statusCode} reachable");
                }
                else
                {
                    endpoint.lastLatencyMs = latency;
                    endpoint.isHealthy = false;
                    return (false, latency, $"HTTP {statusCode} {SecretRedactor.ForMessageWithSecret(response.ReasonPhrase, endpoint.apiKey)}");
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                endpoint.lastLatencyMs = -1;
                endpoint.isHealthy = false;
                // The probe message is shown in the settings UI / debug log: never echo raw exception text.
                return (false, -1, SecretRedactor.ForMessageWithSecret(ex.Message, endpoint.apiKey));
            }
        }
    }
}
