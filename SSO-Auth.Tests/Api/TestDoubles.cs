using System;
using System.Collections.Generic;
using System.Text;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SSO_Auth.Config;
using MediaBrowser.Model.Cryptography;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Api;

/// <summary>
/// Captures every formatted log entry.
/// </summary>
internal sealed class CapturingLogger : ILogger
{
    private readonly List<string> _entries = new();

    /// <summary>
    /// Gets the captured entries, each prefixed with its level.
    /// </summary>
    public IReadOnlyList<string> Entries => _entries;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _entries.Add($"[{logLevel}] {formatter(state, exception)}");
    }
}

/// <summary>
/// A category-typed wrapper around <see cref="CapturingLogger"/>.
/// </summary>
/// <typeparam name="T">The logger category.</typeparam>
internal sealed class TypedCapturingLogger<T> : ILogger<T>
{
    private readonly CapturingLogger _inner = new();

    /// <summary>
    /// Gets the captured entries, each prefixed with its level.
    /// </summary>
    public IReadOnlyList<string> Entries => _inner.Entries;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => _inner.BeginScope(state);

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _inner.Log(logLevel, eventId, state, exception, formatter);
}

/// <summary>
/// A logger whose every level is disabled; <see cref="Log{TState}"/> throws, so a missed
/// <c>IsEnabled</c> guard fails the test instead of silently asserting nothing.
/// </summary>
internal sealed class SilentLogger : ILogger
{
    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => false;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => throw new InvalidOperationException("Log was called although every level is disabled.");
}

/// <summary>
/// Records every password handed to the hash function and answers with a hash deterministic on it.
/// </summary>
/// <remarks>
/// Hand-rolled because <see cref="ICryptoProvider.CreatePasswordHash"/> takes a
/// <c>ReadOnlySpan&lt;char&gt;</c>, which no mocking framework can intercept.
/// </remarks>
internal sealed class RecordingCryptoProvider : ICryptoProvider
{
    private readonly List<string> _hashedPasswords = new();

    /// <summary>
    /// Gets the plaintext passwords handed to the hash function, in call order.
    /// </summary>
    public IReadOnlyList<string> HashedPasswords => _hashedPasswords;

    /// <inheritdoc />
    public string DefaultHashMethod => "PBKDF2";

    /// <summary>
    /// Renders the persisted hash string this provider produces for a given plaintext password.
    /// </summary>
    /// <param name="password">The plaintext password that was hashed.</param>
    /// <returns>The <c>User.Password</c> string for that password.</returns>
    public static string HashOf(string password)
        => new PasswordHash("PBKDF2", Encoding.UTF8.GetBytes(password)).ToString();

    /// <inheritdoc />
    public PasswordHash CreatePasswordHash(ReadOnlySpan<char> password)
    {
        var plaintext = password.ToString();
        _hashedPasswords.Add(plaintext);

        return new PasswordHash("PBKDF2", Encoding.UTF8.GetBytes(plaintext));
    }

    /// <inheritdoc />
    public bool Verify(PasswordHash hash, ReadOnlySpan<char> password) => true;

    /// <inheritdoc />
    public byte[] GenerateSalt() => new byte[16];

    /// <inheritdoc />
    public byte[] GenerateSalt(int length) => new byte[length];
}

/// <summary>
/// Builders for the entities and configuration the sweep tests use.
/// </summary>
internal static class TestModel
{
    /// <summary>The provider id Jellyfin's own user manager provisions accounts with.</summary>
    public const string DefaultProvider = "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider";

    /// <summary>The password reset provider id Jellyfin's own user manager provisions accounts with.</summary>
    public const string DefaultResetProvider = "Jellyfin.Server.Implementations.Users.DefaultPasswordResetProvider";

    /// <summary>
    /// Creates a Jellyfin user entity, optionally already holding a stored password.
    /// </summary>
    /// <param name="username">The account name.</param>
    /// <param name="password">The stored password hash, or null when the account holds none.</param>
    /// <param name="provider">The authentication provider the account is routed at.</param>
    /// <returns>The user entity.</returns>
    public static User NewUser(string username, string? password = null, string? provider = null)
    {
        var user = new User(username, provider ?? DefaultProvider, DefaultResetProvider);
        user.Password = password;
        return user;
    }

    /// <summary>
    /// Adds a canonical link to a provider in the configuration, creating the provider entry if needed.
    /// </summary>
    /// <param name="configuration">The plugin configuration to add the link to.</param>
    /// <param name="mode">The protocol, "oid" or "saml".</param>
    /// <param name="provider">The provider name.</param>
    /// <param name="canonicalName">The identity-provider side of the link.</param>
    /// <param name="userId">The Jellyfin account the link points at.</param>
    public static void Link(PluginConfiguration configuration, string mode, string provider, string canonicalName, Guid userId)
    {
        if (mode == "oid")
        {
            if (!configuration.OidConfigs.TryGetValue(provider, out var oidConfig))
            {
                oidConfig = new OidConfig();
                configuration.OidConfigs[provider] = oidConfig;
            }

            var oidLinks = oidConfig.CanonicalLinks;
            oidLinks[canonicalName] = userId;
            oidConfig.CanonicalLinks = oidLinks;
        }
        else
        {
            if (!configuration.SamlConfigs.TryGetValue(provider, out var samlConfig))
            {
                samlConfig = new SamlConfig();
                configuration.SamlConfigs[provider] = samlConfig;
            }

            var samlLinks = samlConfig.CanonicalLinks;
            samlLinks[canonicalName] = userId;
            samlConfig.CanonicalLinks = samlLinks;
        }
    }
}
