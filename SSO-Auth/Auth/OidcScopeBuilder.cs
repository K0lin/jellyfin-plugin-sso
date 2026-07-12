using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.SSO_Auth.Auth;

/// <summary>
/// Builds the scope list for an OpenID Connect authorization request.
/// </summary>
internal static class OidcScopeBuilder
{
    /// <summary>
    /// Builds a space-separated scope list while always retaining the required openid scope.
    /// </summary>
    /// <param name="configuredScopes">The provider-specific scopes.</param>
    /// <param name="overrideDefaultScopes">Whether to omit the default profile scope.</param>
    /// <returns>The normalized scope list.</returns>
    internal static string Build(IEnumerable<string> configuredScopes, bool overrideDefaultScopes)
    {
        var scopes = new List<string> { "openid" };
        if (!overrideDefaultScopes)
        {
            scopes.Add("profile");
        }

        scopes.AddRange((configuredScopes ?? Array.Empty<string>())
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim()));

        return string.Join(" ", scopes.Distinct(StringComparer.Ordinal));
    }
}
