using System;
using System.Security.Cryptography;
using MediaBrowser.Model.Cryptography;

namespace Jellyfin.Plugin.SSO_Auth.Api;

/// <summary>
/// Mints the unguessable password written to every account the plugin provisions, so the account
/// cannot be signed into with the empty password on the ordinary login form.
/// </summary>
/// <remarks>
/// The create arm in <see cref="SSOController"/> and the start-up sweep both mint through this
/// method, so a swept account gets the same kind of secret as a fresh provisioning. The value is
/// never displayed and never recoverable: nothing is meant to log in with it.
/// </remarks>
internal static class ProvisionedPassword
{
    // 64 bytes from the CSPRNG, base64-encoded, which is what the create arm has always minted.
    // https://jonathancrozier.com/blog/how-to-generate-a-cryptographically-secure-random-string-in-dot-net-with-c-sharp
    private const int EntropyBytes = 64;

    /// <summary>
    /// Mints one random password, already hashed in the form <c>User.Password</c> takes.
    /// </summary>
    /// <param name="cryptoProvider">Jellyfin's crypto provider, so the hash is produced the same way as a real password change.</param>
    /// <returns>The hashed password, ready to assign to <c>User.Password</c>.</returns>
    internal static string Mint(ICryptoProvider cryptoProvider)
    {
        ArgumentNullException.ThrowIfNull(cryptoProvider);

        return cryptoProvider.CreatePasswordHash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(EntropyBytes))).ToString();
    }
}
