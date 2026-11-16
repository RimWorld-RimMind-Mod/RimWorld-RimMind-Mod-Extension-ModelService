using System;
using RimMind.Domain.ValueObjects;

namespace RimMind.ModelService.Protocol
{
    internal static class HttpErrorClassifier
    {
        public static RimMindError ClassifyDefault(int statusCode, string sanitizedBody, string nodeName)
        {
            string errMsg = $"Node '{nodeName}' HTTP {statusCode}: {sanitizedBody}";

            if (statusCode == 400 &&
                !string.IsNullOrEmpty(sanitizedBody) &&
                (sanitizedBody.IndexOf("Content Exists Risk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 sanitizedBody.IndexOf("content_filter", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return RimMindErrors.ContentFiltered(errMsg);
            }

            bool isQuotaOrAuth =
                statusCode == 401 ||
                statusCode == 402 ||
                statusCode == 403 ||
                statusCode == 410 ||
                (!string.IsNullOrEmpty(sanitizedBody) &&
                 (sanitizedBody.IndexOf("Insufficient Balance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  sanitizedBody.IndexOf("Insufficient account funds", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  sanitizedBody.IndexOf("insufficient_quota", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  sanitizedBody.IndexOf("invalid_api_key", StringComparison.OrdinalIgnoreCase) >= 0));

            if (isQuotaOrAuth)
            {
                return RimMindErrors.ClientQuotaOrAuth(errMsg);
            }

            bool isRateLimitOrServer =
                statusCode == 408 ||
                statusCode == 429 ||
                statusCode >= 500 ||
                (!string.IsNullOrEmpty(sanitizedBody) &&
                 (sanitizedBody.IndexOf("rate_limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  sanitizedBody.IndexOf("server_overloaded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  sanitizedBody.IndexOf("concurrency_limit", StringComparison.OrdinalIgnoreCase) >= 0));

            if (isRateLimitOrServer)
            {
                return RimMindErrors.ClientTransient(errMsg);
            }

            return RimMindErrors.ClientPermanent(errMsg);
        }
    }
}
