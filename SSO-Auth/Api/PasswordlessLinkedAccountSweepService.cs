using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO_Auth.Api;

/// <summary>
/// Runs <see cref="PasswordlessLinkedAccountSweep"/> once at host start.
/// </summary>
/// <remarks>
/// Any error is logged and swallowed: a repair that can stop the server from starting is worse
/// than the hole it closes.
/// </remarks>
internal sealed class PasswordlessLinkedAccountSweepService : IHostedService
{
    private readonly IUserManager _userManager;
    private readonly ICryptoProvider _cryptoProvider;
    private readonly ILogger<PasswordlessLinkedAccountSweepService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordlessLinkedAccountSweepService"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="cryptoProvider">The crypto provider that hashes the sealed password.</param>
    /// <param name="logger">The logger.</param>
    public PasswordlessLinkedAccountSweepService(IUserManager userManager, ICryptoProvider cryptoProvider, ILogger<PasswordlessLinkedAccountSweepService> logger)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _cryptoProvider = cryptoProvider ?? throw new ArgumentNullException(nameof(cryptoProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs the sweep at host start.
    /// </summary>
    /// <param name="cancellationToken">Unused; the pass is a bounded walk over the persisted link maps.</param>
    /// <returns>A task that completes when the pass has finished.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var plugin = SSOPlugin.Instance;
        if (plugin is null)
        {
            // The plugin loads before host services start, so this is normally set; skip rather than throw.
            return;
        }

        try
        {
            var sweep = new PasswordlessLinkedAccountSweep(plugin.Configuration, _userManager, _cryptoProvider, _logger);
            await sweep.SweepAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The empty-password door may still be open on those accounts; this line says so.
            _logger.LogError(ex, "The start-up sweep for password-less SSO-linked accounts failed; skipping. Accounts provisioned by an old plugin version may still accept an empty password on the ordinary login form.");
        }
    }

    /// <summary>
    /// No-op on shutdown.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
