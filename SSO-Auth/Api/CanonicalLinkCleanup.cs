using System;
using System.Linq;
using Jellyfin.Plugin.SSO_Auth.Config;

namespace Jellyfin.Plugin.SSO_Auth.Api;

/// <summary>
/// Removes SSO canonical links that point at Jellyfin accounts that no longer exist.
/// </summary>
/// <remarks>
/// Canonical-link maps are keyed by the identity-provider subject (or NameID) and store a Jellyfin
/// user id. Deleting the Jellyfin account does not touch those maps on its own, so a later login
/// can resolve a stale guid and then fail while recreating a user whose name was reused.
/// </remarks>
internal static class CanonicalLinkCleanup
{
    /// <summary>
    /// Removes every canonical link that points at <paramref name="userId"/> from both OpenID and SAML providers.
    /// </summary>
    /// <param name="configuration">The plugin configuration holding the provider link maps.</param>
    /// <param name="userId">The Jellyfin user id whose links should be dropped.</param>
    /// <returns>The number of link entries removed.</returns>
    internal static int RemoveLinksToUser(PluginConfiguration configuration, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var removed = 0;

        if (configuration.OidConfigs is not null)
        {
            foreach (var provider in configuration.OidConfigs.Values)
            {
                removed += RemoveFromLinks(provider.CanonicalLinks, links => provider.CanonicalLinks = links, userId);
            }
        }

        if (configuration.SamlConfigs is not null)
        {
            foreach (var provider in configuration.SamlConfigs.Values)
            {
                removed += RemoveFromLinks(provider.CanonicalLinks, links => provider.CanonicalLinks = links, userId);
            }
        }

        return removed;
    }

    /// <summary>
    /// Drops entries from one provider's link map that point at <paramref name="userId"/>.
    /// </summary>
    /// <param name="links">The provider's current link map (may be a fresh empty dictionary from the getter).</param>
    /// <param name="assign">Writes the mutated map back onto the provider so a null backing field is populated.</param>
    /// <param name="userId">The Jellyfin user id to unlink.</param>
    /// <returns>The number of entries removed from this map.</returns>
    private static int RemoveFromLinks(
        SerializableDictionary<string, Guid> links,
        Action<SerializableDictionary<string, Guid>> assign,
        Guid userId)
    {
        if (links is null || links.Count == 0)
        {
            return 0;
        }

        var keys = links.Where(pair => pair.Value == userId).Select(pair => pair.Key).ToList();
        if (keys.Count == 0)
        {
            return 0;
        }

        foreach (var key in keys)
        {
            links.Remove(key);
        }

        assign(links);
        return keys.Count;
    }
}
