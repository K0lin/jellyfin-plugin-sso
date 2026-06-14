using System.Collections.Generic;

namespace Jellyfin.Plugin.SSO_Auth.Auth;

/// <summary>
/// Result of evaluating provider roles against plugin authorization settings.
/// </summary>
internal sealed class AuthorizationEvaluation
{
    /// <summary>
    /// Gets a value indicating whether the user is allowed to authenticate.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user should be treated as a Jellyfin administrator.
    /// </summary>
    public bool IsAdmin { get; init; }

    /// <summary>
    /// Gets the folder ids allowed by the matched roles or default provider settings.
    /// </summary>
    public IReadOnlyList<string> Folders { get; init; } = new List<string>();

    /// <summary>
    /// Gets a value indicating whether Live TV access should be enabled.
    /// </summary>
    public bool EnableLiveTv { get; init; }

    /// <summary>
    /// Gets a value indicating whether Live TV management should be enabled.
    /// </summary>
    public bool EnableLiveTvManagement { get; init; }
}
