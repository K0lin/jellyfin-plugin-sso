using System;
using System.Linq;
using MediaBrowser.Model.Users;

namespace Jellyfin.Plugin.SSO_Auth.Auth;

/// <summary>
/// Applies the provider's allowed-tags list to an account the plugin has just created.
/// </summary>
internal static class NewUserTagPolicy
{
    /// <summary>
    /// Sets <see cref="UserPolicy.AllowedTags"/> to the configured list when the user was created
    /// during the current SSO flow. Existing and adopted accounts, and an empty list, leave the
    /// policy's tags as they are.
    /// </summary>
    /// <param name="policy">The policy about to be written for the user.</param>
    /// <param name="newUserAllowedTags">The configured tags, or null.</param>
    /// <param name="isNewUser">Whether the user was created during the current SSO flow.</param>
    /// <returns>Whether the policy's allowed tags were set.</returns>
    internal static bool Apply(UserPolicy policy, string[] newUserAllowedTags, bool isNewUser)
    {
        if (!isNewUser || newUserAllowedTags == null)
        {
            return false;
        }

        var tags = newUserAllowedTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (tags.Length == 0)
        {
            return false;
        }

        policy.AllowedTags = tags;
        return true;
    }
}
