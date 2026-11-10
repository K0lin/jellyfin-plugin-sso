using System;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO_Auth.Api;

/// <summary>
/// Clears canonical links when Jellyfin deletes the linked account.
/// </summary>
internal sealed class UserDeletedCanonicalLinkConsumer : IEventConsumer<UserDeletedEventArgs>
{
    private readonly ILogger<UserDeletedCanonicalLinkConsumer> _logger;

    public UserDeletedCanonicalLinkConsumer(ILogger<UserDeletedCanonicalLinkConsumer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task OnEvent(UserDeletedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);

        if (SSOPlugin.Instance is not { } plugin || eventArgs.Argument is not { } user)
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
