using System;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Cryptography;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO_Auth.Api;

/// <summary>
/// Seals an account that holds no stored password by giving it an unguessable one, so the ordinary
/// login form stops accepting the empty password for it.
/// </summary>
/// <remarks>
/// Shared by the create arm, the login-time repair and the start-up sweep, so every writer seals an
/// account the same way. The login routing is never touched and an existing password is kept.
/// </remarks>
internal static class PasswordlessAccountSealer
{
    /// <summary>
    /// Seals the account if it holds no stored password.
    /// </summary>
    /// <param name="user">The account to seal.</param>
    /// <param name="userManager">The user manager that persists the account.</param>
    /// <param name="cryptoProvider">The crypto provider that hashes the sealed password.</param>
    /// <returns>True when a password was minted and persisted; false when the account already had one.</returns>
    internal static async Task<bool> SealAsync(User user, IUserManager userManager, ICryptoProvider cryptoProvider)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(cryptoProvider);

        // An empty stored password is the door; a real one an administrator set is left alone.
        if (!string.IsNullOrEmpty(user.Password))
        {
            return false;
        }

        user.Password = ProvisionedPassword.Mint(cryptoProvider);
        await userManager.UpdateUserAsync(user).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Seals a newly created account, removing it again if the write fails so the login fails closed.
    /// </summary>
    /// <param name="user">The account created by the current SSO flow.</param>
    /// <param name="userManager">The user manager that persists the account.</param>
    /// <param name="cryptoProvider">The crypto provider that hashes the sealed password.</param>
    /// <param name="logger">The logger for a rollback that itself failed.</param>
    /// <returns>A task that completes when the account is persisted.</returns>
    internal static async Task SealNewAccountAsync(User user, IUserManager userManager, ICryptoProvider cryptoProvider, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            var sealedNow = await SealAsync(user, userManager, cryptoProvider).ConfigureAwait(false);
            if (!sealedNow)
            {
                // The account already holds a password; only the login routing still needs persisting.
                await userManager.UpdateUserAsync(user).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Remove the half-created account so the login fails closed rather than leave an enabled,
            // unlinked account with no password behind.
            try
            {
                await userManager.DeleteUserAsync(user.Id).ConfigureAwait(false);
            }
            catch (Exception deleteException)
            {
                // The account is stranded; naming it is what tells an administrator which one to repair.
                logger.LogError(deleteException, "Could not remove SSO user {Username} after its password failed to persist; the account accepts an empty password until one is set.", user.Username);
            }

            throw;
        }
    }
}
