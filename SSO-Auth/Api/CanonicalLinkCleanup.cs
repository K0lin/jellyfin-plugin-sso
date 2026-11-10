using System;
using System.Linq;
using Jellyfin.Plugin.SSO_Auth.Config;

namespace Jellyfin.Plugin.SSO_Auth.Api;

/// <summary>
/// Removes canonical links that point at a deleted Jellyfin user.
/// </summary>
internal static class CanonicalLinkCleanup
{
    internal static int RemoveLinksToUser(PluginConfiguration configuration, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var removed = 0;

        if (configuration.OidConfigs is not null)
        {
            foreach (var provider in configuration.OidConfigs.Values)
            {
                removed += RemoveFromLinks(provider.CanonicalLinks, userId);
            }
        }

        if (configuration.SamlConfigs is not null)
        {
            foreach (var provider in configuration.SamlConfigs.Values)
            {
                removed += RemoveFromLinks(provider.CanonicalLinks, userId);
            }
        }

        return removed;
    }

    private static int RemoveFromLinks(SerializableDictionary<string, Guid> links, Guid userId)
    {
        if (links is null || links.Count == 0)
        {
            return 0;
        }

        var removed = 0;
        foreach (var key in links.Where(pair => pair.Value == userId).Select(pair => pair.Key).ToList())
        {
            links.Remove(key);
            removed++;
        }

        return removed;
    }
}
