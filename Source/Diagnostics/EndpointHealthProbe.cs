using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RimMind.ModelService.Models;
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

        public static async Task<(bool success, int latencyMs, string message)> ProbeEndpointAsync(
            ModelEndpointConfig endpoint,
            CancellationToken ct = default)
        {
            if (endpoint == null || string.IsNullOrWhiteSpace(endpoint.endpoint))
            {
                return (false, -1, "Invalid or empty endpoint URL");
            }

            // Security check for local subscription gateway
            if (endpoint.providerType == ProviderType.LocalSubscriptionGateway)
            {
                if (!LocalLoopbackValidator.IsLoopbackAddress(endpoint.endpoint))
                {
                    return (false, -1, "Security violation: Local subscription gateways must use loopback address (127.0.0.1)");
                }
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.endpoint.TrimEnd('/') + "/models");
                if (!string.IsNullOrEmpty(endpoint.apiKey))
                {
                    if (endpoint.providerType == ProviderType.AnthropicClaude)
                    {
                        request.Headers.TryAddWithoutValidation("x-api-key", endpoint.apiKey);
                        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                    }
                    else
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + endpoint.apiKey);
                    }
                }
                if (!string.IsNullOrEmpty(endpoint.endpoint) && (endpoint.endpoint.IndexOf("opencode", StringComparison.OrdinalIgnoreCase) >= 0 || (!string.IsNullOrEmpty(endpoint.apiKey) && endpoint.apiKey.IndexOf("oc_sk_", StringComparison.OrdinalIgnoreCase) >= 0)))
                {
                    request.Headers.TryAddWithoutValidation("x-opencode-session", "rimmind-probe");
                }

                using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                stopwatch.Stop();
                int latency = (int)stopwatch.ElapsedMilliseconds;

                // Even if 401 or 404 is returned, the endpoint is reachable at the network level
                if (response.IsSuccessStatusCode || (int)response.StatusCode == 401 || (int)response.StatusCode == 404)
                {
                    endpoint.RecordSuccess(latency);
                    return (true, latency, $"HTTP {(int)response.StatusCode} reachable");
                }
                else
                {
                    endpoint.lastLatencyMs = latency;
                    return (false, latency, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                endpoint.lastLatencyMs = -1;
                return (false, -1, ex.Message);
            }
        }
    }
}
