using System.Collections.Generic;
using RimMind.ModelService.Client;
using RimMind.ModelService.Models;
using Xunit;

namespace RimMind.ModelService.Tests.Contracts
{
    public class EnvironmentEndpointOverlayContracts
    {
        private static readonly Dictionary<string, string> Env = new Dictionary<string, string>
        {
            ["RIMMIND_TEST_API_KEY"] = "sk-env-secret",
            ["RIMMIND_TEST_ENDPOINT"] = "https://env.example/v1",
            ["RIMMIND_TEST_MODEL"] = "env-model",
        };

        [Fact]
        public void Overlay_never_mutates_persisted_endpoints_and_keeps_runtime_state_across_calls()
        {
            var overlay = EnvironmentEndpointOverlay.FromEnvironment(k => Env.TryGetValue(k, out var v) ? v : null);
            Assert.NotNull(overlay);

            var keyless = new ModelEndpointConfig { id = "go", providerType = ProviderType.OpenCodeGo, apiKey = "", endpoint = "https://user/v1", modelName = "user-model" };
            var keyed = new ModelEndpointConfig { id = "own", providerType = ProviderType.OpenAICompatible, apiKey = "sk-user" };
            var persisted = new List<ModelEndpointConfig> { keyless, keyed };

            var first = overlay!.Apply(persisted);

            // Persisted settings stay exactly as the player configured them (nothing secret gets saved).
            Assert.Equal("", keyless.apiKey);
            Assert.Equal("https://user/v1", keyless.endpoint);
            Assert.Equal("user-model", keyless.modelName);

            Assert.NotSame(keyless, first[0]);
            Assert.Equal("sk-env-secret", first[0].apiKey);
            Assert.Equal("https://env.example/v1", first[0].endpoint);
            Assert.Equal("env-model", first[0].modelName);
            Assert.Same(keyed, first[1]);

            // Circuit-breaker state recorded on the shadow survives the next request's Apply.
            first[0].RecordFailure(1000);
            var second = overlay.Apply(persisted);
            Assert.Same(first[0], second[0]);
            Assert.Equal(1, second[0].consecutiveFailures);
        }

        [Fact]
        public void No_environment_key_means_no_overlay()
        {
            Assert.Null(EnvironmentEndpointOverlay.FromEnvironment(_ => null));
        }
    }
}
