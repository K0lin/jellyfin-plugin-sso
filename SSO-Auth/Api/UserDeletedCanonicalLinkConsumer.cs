using System;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO_Auth.Api;

/// <summary>
/// Drops SSO canonical links when Jellyfin deletes the account they point at.
/// </summary>
internal sealed class UserDeletedCanonicalLinkConsumer : IEventConsumer<UserDeletedEventArgs>
{
    private readonly ILogger<UserDeletedCanonicalLinkConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDeletedCanonicalLinkConsumer"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public UserDeletedCanonicalLinkConsumer(ILogger<UserDeletedCanonicalLinkConsumer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task OnEvent(UserDeletedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);

        var plugin = SSOPlugin.Instance;
        if (plugin is null)
        {
            // The plugin loads before host services start, so this is normally set; skip rather than throw.
            return Task.CompletedTask;
        }

        var user = eventArgs.Argument;
        if (user is null)
        {
            return Task.CompletedTask;
        }

        var configuration = plugin.Configuration;
        var removed = CanonicalLinkCleanup.RemoveLinksToUser(configuration, user.Id);
        if (removed == 0)
        {
            return Task.CompletedTask;
        }

        plugin.UpdateConfiguration(configuration);
        _logger.LogInformation(
            "Removed {Count} SSO canonical link(s) for deleted user {UserId}",
            removed,
            user.Id);

        return Task.CompletedTask;
    }
}
