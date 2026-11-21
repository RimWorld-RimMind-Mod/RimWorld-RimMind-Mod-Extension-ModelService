using RimMind.Application.Features.Llm;

namespace RimMind.ModelService.Security
{
    /// <summary>
    /// Credential redaction for ModelService error messages and diagnostics.
    /// Delegates directly to Core's public <see cref="LogSecretRedactor"/> so redaction patterns,
    /// truncation markers, and literal-secret masking stay unified without touching Core Infrastructure.
    /// </summary>
    internal static class SecretRedactor
    {
        public const int DefaultMaxChars = LogSecretRedactor.DefaultMaxChars;

        public static string Redact(string? text)
            => LogSecretRedactor.Redact(text);

        public static string ForLog(string? text, int maxChars = DefaultMaxChars)
            => LogSecretRedactor.ForLog(text, maxChars);

        public static string ForMessage(string? text, int maxChars = DefaultMaxChars)
            => LogSecretRedactor.ForMessage(text, maxChars);

        public static string ForMessageWithSecret(string? text, string? knownSecret, int maxChars = DefaultMaxChars)
            => LogSecretRedactor.ForMessageWithSecret(text, knownSecret, maxChars);
    }
}
