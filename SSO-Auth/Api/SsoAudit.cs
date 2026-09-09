using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO_Auth.Api;

/// <summary>
/// Emits the plugin's audit-log entries. Every entry shares the "[SSO Audit]" prefix so operators
/// can filter the trail, and no entry logs a secret or an account name.
/// </summary>
internal static class SsoAudit
{
    /// <summary>
    /// Records the start-up sweep sealing SSO-linked accounts that had no stored password. Logged
    /// with a count only, and only when something was actually sealed.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="sealedAccounts">How many accounts the sweep gave a password to.</param>
    internal static void PasswordlessAccountsSealed(ILogger logger, int sealedAccounts)
    {
        if (!logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        logger.LogWarning(
            "[SSO Audit] Sealed {Count} SSO-linked account(s) that had no stored password: an account with none accepts the empty password on the ordinary login form, so these were reachable without the identity provider. Each was given an unguessable password that nothing knows and nothing can recover; their login provider routing was left exactly as it was. Any SSO-linked account that held no stored password is in this set, whatever plugin version created it.",
            sealedAccounts);
    }

    /// <summary>
    /// Records a linked account being sealed at login because it held no stored password. Logged
    /// without the account name, like the sweep line: the account accepted the empty password on
    /// the ordinary login form until this login.
    /// </summary>
    /// <param name="logger">The logger.</param>
    internal static void PasswordlessAccountSealedAtLogin(ILogger logger)
    {
        if (!logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        logger.LogWarning(
            "[SSO Audit] Sealed an SSO-linked account at login that had no stored password: an account with none accepts the empty password on the ordinary login form, so it was reachable without the identity provider until this login. It was given an unguessable password that nothing knows and nothing can recover; its login provider routing was left exactly as it was.");
    }
}
