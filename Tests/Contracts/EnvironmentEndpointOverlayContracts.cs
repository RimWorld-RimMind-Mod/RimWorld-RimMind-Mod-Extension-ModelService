using System.Collections.Generic;
using RimMind.ModelService.Client;
using RimMind.ModelService.Models;
using RimMind.ModelService.Protocol;
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

        [Fact]
        public void Custom_protocol_EnvOverrideBinding_is_discovered_dynamically()
        {
            var custom = new CustomEnvBoundProtocol();
            ModelProtocolRegistry.Register(custom);
            try
            {
                var customEnv = new Dictionary<string, string>
                {
                    ["RIMMIND_CUSTOM_PROTO_KEY"] = "sk-custom-env-123",
                    ["RIMMIND_CUSTOM_PROTO_URL"] = "https://custom-env.example/v1",
                    ["RIMMIND_CUSTOM_PROTO_MODEL"] = "custom-v2"
                };
                var overlay = EnvironmentEndpointOverlay.FromEnvironment(k => customEnv.TryGetValue(k, out var v) ? v : null);
                Assert.NotNull(overlay);

                var node = new ModelEndpointConfig { id = "c1", protocolId = custom.ProtocolId, apiKey = "" };
                var applied = overlay!.Apply(new[] { node });
                Assert.Equal(string.Empty, node.apiKey);
                Assert.Equal("sk-custom-env-123", applied[0].apiKey);
                Assert.Equal("https://custom-env.example/v1", applied[0].endpoint);
                Assert.Equal("custom-v2", applied[0].modelName);
            }
            finally
            {
                ModelProtocolRegistry.Unregister(custom.ProtocolId);
            }
        }

        private sealed class CustomEnvBoundProtocol : OpenAIModelProtocol
        {
            private static readonly ProtocolEnvBinding Binding = new ProtocolEnvBinding(
                apiKeyVars: new[] { "RIMMIND_CUSTOM_PROTO_KEY" },
                endpointVars: new[] { "RIMMIND_CUSTOM_PROTO_URL" },
                modelVars: new[] { "RIMMIND_CUSTOM_PROTO_MODEL" });

            public override string ProtocolId => "custom_env_bound";
            public override string FallbackLabel => "Custom Env Bound";
            public override ProtocolEnvBinding? EnvOverrideBinding => Binding;
        }
    }
}
