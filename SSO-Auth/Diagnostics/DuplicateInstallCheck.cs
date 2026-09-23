using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO_Auth.Diagnostics;

/// <summary>
/// Finds additional copies of this plugin's assembly in the running process.
/// </summary>
/// <remarks>
/// Jellyfin loads every enabled plugin directory into its own load context and hands each resulting
/// assembly to MVC as an application part (<c>ApplicationHost.GetApiPluginAssemblies</c> feeds
/// <c>ApiServiceCollectionExtensions.AddJellyfinApi</c>). Two directories holding <c>SSO-Auth.dll</c>
/// therefore register <see cref="Api.SSOController"/> twice, and every request to a route of this
/// plugin then fails with an <c>AmbiguousMatchException</c> and HTTP 500. The plugin cannot undo that
/// registration from inside, so it reports the offending paths instead of leaving the operator with an
/// unexplained 500.
/// </remarks>
internal static class DuplicateInstallCheck
{
    /// <summary>
    /// The simple name of the assembly this plugin ships as.
    /// </summary>
    internal const string AssemblySimpleName = "SSO-Auth";

    /// <summary>
    /// Returns the distinct file locations the plugin assembly has been loaded from.
    /// </summary>
    /// <param name="loadedAssemblies">The assemblies currently loaded in the process.</param>
    /// <returns>The sorted, distinct locations. More than one entry means the install is broken.</returns>
    internal static IReadOnlyList<string> FindLoadedCopies(IEnumerable<Assembly> loadedAssemblies)
    {
        ArgumentNullException.ThrowIfNull(loadedAssemblies);

        return loadedAssemblies
            .Where(assembly => string.Equals(assembly.GetName().Name, AssemblySimpleName, StringComparison.Ordinal))
            .Select(assembly => assembly.Location)
            .Where(location => !string.IsNullOrEmpty(location))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(location => location, StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>
/// Logs a diagnostic at host start when more than one copy of this plugin is loaded.
/// </summary>
internal sealed class DuplicateInstallWarningService : IHostedService
{
    private readonly ILogger<DuplicateInstallWarningService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateInstallWarningService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public DuplicateInstallWarningService(ILogger<DuplicateInstallWarningService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs the check at host start.
    /// </summary>
    /// <param name="cancellationToken">Unused; the check only inspects already loaded assemblies.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var copies = DuplicateInstallCheck.FindLoadedCopies(AppDomain.CurrentDomain.GetAssemblies());
        if (copies.Count > 1)
        {
            _logger.LogError(
                "Jellyfin has loaded {Count} copies of the SSO plugin assembly: {Locations}. Every route of this plugin will fail with an AmbiguousMatchException and HTTP 500 until the stale plugin directory is removed from the Jellyfin data directory and the server is restarted.",
                copies.Count,
                string.Join(", ", copies));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// No-op on shutdown.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
