using RimMind.Infrastructure.Services.Clients.Shared;

namespace RimMind.ModelService.Security
{
    /// <summary>
    /// Credential redaction for ModelService error messages and diagnostics.
    /// Delegates directly to Core's <see cref="LogSanitizer"/> so redaction patterns,
    /// truncation markers, and literal-secret masking stay unified across Core and ModelService.
    /// </summary>
    internal static class SecretRedactor
    {
        public const int DefaultMaxChars = LogSanitizer.DefaultMaxChars;

        public static string Redact(string? text)
            => LogSanitizer.Redact(text);

        public static string ForLog(string? text, int maxChars = DefaultMaxChars)
            => LogSanitizer.ForLog(text, maxChars);

        public static string ForMessage(string? text, int maxChars = DefaultMaxChars)
            => LogSanitizer.ForLog(text, maxChars);

        public static string ForMessageWithSecret(string? text, string? knownSecret, int maxChars = DefaultMaxChars)
            => LogSanitizer.ForMessageWithSecret(text, knownSecret, maxChars);
    }
}
