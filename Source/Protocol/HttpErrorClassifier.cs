using RimMind.Domain.ValueObjects;
using CoreHttpErrorClassifier = RimMind.Application.Features.Llm.HttpErrorClassifier;

namespace RimMind.ModelService.Protocol
{
    /// <summary>
    /// Backwards-compatible forwarding shim around Core's public
    /// <see cref="RimMind.Application.Features.Llm.HttpErrorClassifier"/>.
    /// Candidate dead file once any remaining internal references are confirmed unused.
    /// </summary>
    internal static class HttpErrorClassifier
    {
        public static RimMindError ClassifyDefault(int statusCode, string sanitizedBody, string nodeName) =>
            CoreHttpErrorClassifier.Classify(statusCode, sanitizedBody, nodeName);
    }
}
