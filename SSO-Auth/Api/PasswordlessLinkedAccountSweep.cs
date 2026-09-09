using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SSO_Auth.Config;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Cryptography;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO_Auth.Api;

/// <summary>
/// Seals every SSO-linked account that has no stored password by giving it an unguessable one, so
/// the ordinary login form stops accepting the empty password for it.
/// </summary>
/// <remarks>
/// The create arm only runs when an account is new, so accounts provisioned without a password, or
/// password-less accounts an SSO identity was later linked to, keep that door open. The pass never
/// touches <c>AuthenticationProviderId</c>, never overwrites an existing password, and is
/// idempotent, so it can run on every boot.
/// </remarks>
internal sealed class PasswordlessLinkedAccountSweep
{
    private readonly PluginConfiguration _configuration;
    private readonly IUserManager _userManager;
    private readonly ICryptoProvider _cryptoProvider;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordlessLinkedAccountSweep"/> class.
    /// </summary>
    /// <param name="configuration">The plugin configuration holding the canonical links.</param>
    /// <param name="userManager">The user manager used to resolve and persist each account.</param>
    /// <param name="cryptoProvider">The crypto provider that hashes the sealed password.</param>
    /// <param name="logger">The logger the audit line is written to.</param>
    internal PasswordlessLinkedAccountSweep(PluginConfiguration configuration, IUserManager userManager, ICryptoProvider cryptoProvider, ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _cryptoProvider = cryptoProvider ?? throw new ArgumentNullException(nameof(cryptoProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs one pass and returns how many accounts it sealed.
    /// </summary>
    /// <returns>The number of accounts given a password by this pass.</returns>
    internal async Task<int> SweepAsync()
    {
        var sealedAccounts = 0;

        foreach (var userId in LinkedUserIds())
        {
            // A link can outlive its account; skip it rather than fail the whole pass.
            if (_userManager.GetUserById(userId) is not { } user)
            {
                continue;
            }

            if (await PasswordlessAccountSealer.SealAsync(user, _userManager, _cryptoProvider).ConfigureAwait(false))
            {
                sealedAccounts++;
            }
        }

        // One line for the whole pass, with a count only, and nothing when there was nothing to seal.
        if (sealedAccounts > 0)
        {
            SsoAudit.PasswordlessAccountsSealed(_logger, sealedAccounts);
        }

        return sealedAccounts;
    }

    /// <summary>
    /// Collects the distinct user ids every provider holds a canonical link for, across both protocols.
    /// </summary>
    /// <returns>The distinct user ids the canonical-link maps point at.</returns>
    private IEnumerable<Guid> LinkedUserIds()
    {
        var linkedUserIds = new HashSet<Guid>();

        if (_configuration.OidConfigs is not null)
        {
            foreach (var provider in _configuration.OidConfigs.Values)
            {
                AddLinkTargets(linkedUserIds, provider.CanonicalLinks);
            }
        }

        if (_configuration.SamlConfigs is not null)
        {
            foreach (var provider in _configuration.SamlConfigs.Values)
            {
                AddLinkTargets(linkedUserIds, provider.CanonicalLinks);
            }
        }

        return linkedUserIds;
    }

    /// <summary>
    /// Adds the user ids one provider's link map points at. A provider with no links contributes nothing.
    /// </summary>
    /// <param name="linkedUserIds">The accumulating set of linked user ids.</param>
    /// <param name="canonicalLinks">The provider's canonical-link map, or null when it has none.</param>
    private static void AddLinkTargets(HashSet<Guid> linkedUserIds, SerializableDictionary<string, Guid> canonicalLinks)
    {
        if (canonicalLinks is null)
        {
            return;
        }

        foreach (var userId in canonicalLinks.Values)
        {
            linkedUserIds.Add(userId);
        }
    }
}
