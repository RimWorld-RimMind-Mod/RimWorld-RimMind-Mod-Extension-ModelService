using System;
using System.Collections.Generic;
using System.Net;

namespace RimMind.ModelService.Security
{
    public static class LocalLoopbackValidator
    {
        private static readonly HashSet<string> ValidLoopbackHostnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "localhost",
            "localhost."
        };

        /// <summary>
        /// Validates whether a given URL string points strictly to a local loopback address.
        /// Used to safeguard subscription-to-API and Codex local proxies from leaking credentials to external networks.
        /// </summary>
        public static bool IsLoopbackAddress(string? urlString)
        {
            if (string.IsNullOrWhiteSpace(urlString))
                return false;

            try
            {
                if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri))
                {
                    if (!Uri.TryCreate("http://" + urlString, UriKind.Absolute, out uri))
                        return false;
                }

                // 1. Strictly require http or https schemes
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                    return false;

                // 2. Validate hostname (localhost or FQDN localhost.)
                string host = uri.DnsSafeHost.TrimEnd('.');
                if (ValidLoopbackHostnames.Contains(host))
                    return true;

                // 3. Validate network IP loopback (RFC 1122 127.0.0.0/8 and IPv6 ::1 loopbacks)
                string rawHost = uri.Host.Trim('[', ']');
                if (IPAddress.TryParse(rawHost, out var ip))
                {
                    return IPAddress.IsLoopback(ip);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
