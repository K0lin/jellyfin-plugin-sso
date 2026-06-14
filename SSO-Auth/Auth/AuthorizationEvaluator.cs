using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SSO_Auth.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.SSO_Auth.Auth;

/// <summary>
/// Evaluates provider role claims against plugin authorization settings.
/// </summary>
internal static class AuthorizationEvaluator
{
    /// <summary>
    /// Extracts OIDC roles from the configured role claim path.
    /// </summary>
    /// <param name="claims">The OIDC claims returned by the provider.</param>
    /// <param name="roleClaim">The configured role claim path. Dots may be escaped with a backslash.</param>
    /// <returns>The roles found in the configured claim.</returns>
    internal static IReadOnlyList<string> ExtractOidcRoles(IEnumerable<Claim> claims, string roleClaim)
    {
        string[] segments = string.IsNullOrEmpty(roleClaim) ? Array.Empty<string>() : Regex.Split(roleClaim.Trim(), "(?<!\\\\)\\.");
        if (segments.Length == 0)
        {
            return Array.Empty<string>();
        }

        segments = segments.Select(i => i.Replace("\\.", ".")).ToArray();
        var roles = new List<string>();

        foreach (var claim in claims ?? Array.Empty<Claim>())
        {
            if (claim.Type != segments[0])
            {
                continue;
            }

            if (segments.Length == 1)
            {
                roles.Add(claim.Value);
                continue;
            }

            roles.AddRange(ExtractRolesFromJsonClaim(claim.Value, segments));
        }

        return roles;
    }

    /// <summary>
    /// Evaluates extracted provider roles against configured RBAC rules.
    /// </summary>
    /// <param name="roles">The roles extracted from the provider response.</param>
    /// <param name="allowedRoles">The roles allowed to authenticate.</param>
    /// <param name="adminRoles">The roles granting administrator permissions.</param>
    /// <param name="defaultFolders">The folders allowed when folder roles are disabled.</param>
    /// <param name="enableFolderRoles">Whether folder access is controlled by role mappings.</param>
    /// <param name="folderRoleMapping">The configured folder role mappings.</param>
    /// <param name="defaultLiveTv">Whether Live TV access is enabled by default.</param>
    /// <param name="defaultLiveTvManagement">Whether Live TV management is enabled by default.</param>
    /// <param name="enableLiveTvRoles">Whether Live TV permissions are controlled by roles.</param>
    /// <param name="liveTvRoles">The roles granting Live TV access.</param>
    /// <param name="liveTvManagementRoles">The roles granting Live TV management.</param>
    /// <returns>The evaluated authorization settings.</returns>
    internal static AuthorizationEvaluation Evaluate(
        IEnumerable<string> roles,
        IReadOnlyCollection<string> allowedRoles,
        IReadOnlyCollection<string> adminRoles,
        IEnumerable<string> defaultFolders,
        bool enableFolderRoles,
        IEnumerable<FolderRoleMap> folderRoleMapping,
        bool defaultLiveTv,
        bool defaultLiveTvManagement,
        bool enableLiveTvRoles,
        IReadOnlyCollection<string> liveTvRoles,
        IReadOnlyCollection<string> liveTvManagementRoles)
    {
        var roleList = roles?.ToList() ?? new List<string>();
        var folders = enableFolderRoles ? new List<string>() : new List<string>(defaultFolders ?? Array.Empty<string>());
        bool isValid = allowedRoles == null || allowedRoles.Count == 0;
        bool isAdmin = false;
        bool enableLiveTv = defaultLiveTv;
        bool enableLiveTvManagement = defaultLiveTvManagement;

        foreach (string role in roleList)
        {
            if (allowedRoles != null && allowedRoles.Any(allowedRole => role.Equals(allowedRole)))
            {
                isValid = true;
            }

            if (adminRoles != null && adminRoles.Any(adminRole => role.Equals(adminRole)))
            {
                isAdmin = true;
            }

            if (enableFolderRoles)
            {
                foreach (FolderRoleMap folderRoleMap in folderRoleMapping ?? Array.Empty<FolderRoleMap>())
                {
                    if (role.Equals(folderRoleMap.Role?.Trim()))
                    {
                        folders.AddRange(folderRoleMap.Folders ?? new List<string>());
                    }
                }
            }

            if (enableLiveTvRoles)
            {
                if (liveTvRoles != null && liveTvRoles.Any(liveTvRole => role.Equals(liveTvRole)))
                {
                    enableLiveTv = true;
                }

                if (liveTvManagementRoles != null && liveTvManagementRoles.Any(liveTvManagementRole => role.Equals(liveTvManagementRole)))
                {
                    enableLiveTvManagement = true;
                }
            }
        }

        return new AuthorizationEvaluation
        {
            IsValid = isValid,
            IsAdmin = isAdmin,
            Folders = folders,
            EnableLiveTv = enableLiveTv,
            EnableLiveTvManagement = enableLiveTvManagement
        };
    }

    private static IReadOnlyList<string> ExtractRolesFromJsonClaim(string claimValue, IReadOnlyList<string> segments)
    {
        var json = JsonConvert.DeserializeObject<IDictionary<string, object>>(claimValue);
        if (json is null)
        {
            return Array.Empty<string>();
        }

        for (int i = 1; i < segments.Count - 1; i++)
        {
            var segment = segments[i];
            if (!json.TryGetValue(segment, out var nextToken) || nextToken is not JObject nextObject)
            {
                return Array.Empty<string>();
            }

            json = nextObject.ToObject<IDictionary<string, object>>();
            if (json is null)
            {
                return Array.Empty<string>();
            }
        }

        if (!json.TryGetValue(segments[^1], out var rolesToken) || rolesToken is not JArray rolesArray)
        {
            return Array.Empty<string>();
        }

        return rolesArray.ToObject<List<string>>() ?? new List<string>();
    }
}
