namespace Jellyfin.Plugin.SSO_Auth.Auth;

/// <summary>
/// Resolves whether an SSO login should update the Jellyfin content download permission.
/// </summary>
internal static class ContentDownloadPolicy
{
    /// <summary>
    /// Resolves the configured permission for the current user provisioning state.
    /// </summary>
    /// <param name="enableContentDownloading">The configured permission, or null to leave it unchanged.</param>
    /// <param name="isNewUser">Whether the user was created during the current SSO flow.</param>
    /// <param name="applyOnEveryLogin">Whether the permission should be applied to existing users on login.</param>
    /// <returns>The policy value to apply, or null when the existing value must be preserved.</returns>
    internal static bool? Resolve(bool? enableContentDownloading, bool isNewUser, bool applyOnEveryLogin)
    {
        if (!enableContentDownloading.HasValue || (!isNewUser && !applyOnEveryLogin))
        {
            return null;
        }

        return enableContentDownloading.Value;
    }
}
