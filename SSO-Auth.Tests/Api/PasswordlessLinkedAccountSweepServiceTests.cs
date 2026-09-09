using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SSO_Auth.Api;
using Jellyfin.Plugin.SSO_Auth.Config;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Serialization;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Api;

public class PasswordlessLinkedAccountSweepServiceTests : IDisposable
{
    private readonly IUserManager _users = Substitute.For<IUserManager>();
    private readonly RecordingCryptoProvider _crypto = new();
    private readonly TypedCapturingLogger<PasswordlessLinkedAccountSweepService> _logger = new();
    private readonly List<string> _temporaryDirectories = new();

    [Fact]
    public void Constructor_ThrowsWhenAnyDependencyIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new PasswordlessLinkedAccountSweepService(null!, _crypto, _logger));
        Assert.Throws<ArgumentNullException>(() => new PasswordlessLinkedAccountSweepService(_users, null!, _logger));
        Assert.Throws<ArgumentNullException>(() => new PasswordlessLinkedAccountSweepService(_users, _crypto, null!));
    }

    [Fact]
    public async Task StartAsync_WithoutLoadedPluginInstance_DoesNothing()
    {
        ResetPluginInstance();

        var service = new PasswordlessLinkedAccountSweepService(_users, _crypto, _logger);

        await service.StartAsync(CancellationToken.None);

        _users.DidNotReceiveWithAnyArgs().GetUserById(Arg.Any<Guid>());
        await _users.DidNotReceiveWithAnyArgs().UpdateUserAsync(Arg.Any<User>());
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task StartAsync_SealsPasswordlessLinkedAccountsOfTheLoadedPlugin()
    {
        ResetPluginInstance();
        var plugin = CreatePlugin();
        try
        {
            var alice = TestModel.NewUser("alice");
            TestModel.Link(plugin.Configuration, "oid", "keycloak", "alice", alice.Id);
            _users.GetUserById(alice.Id).Returns(alice);

            var service = new PasswordlessLinkedAccountSweepService(_users, _crypto, _logger);
            await service.StartAsync(CancellationToken.None);

            Assert.Equal(RecordingCryptoProvider.HashOf(_crypto.HashedPasswords[0]), alice.Password);
            await _users.Received(1).UpdateUserAsync(alice);
            Assert.Contains("Sealed 1 SSO-linked account(s)", Assert.Single(_logger.Entries));
        }
        finally
        {
            ResetPluginInstance();
        }
    }

    [Fact]
    public async Task StartAsync_WhenTheSweepThrows_LogsAndDoesNotStopTheHost()
    {
        ResetPluginInstance();
        var plugin = CreatePlugin();
        try
        {
            var deadAccountId = Guid.NewGuid();
            TestModel.Link(plugin.Configuration, "saml", "keycloak", "alice", deadAccountId);
            _users
                .When(userManager => userManager.GetUserById(deadAccountId))
                .Throw(new InvalidOperationException("user store unreachable"));

            var service = new PasswordlessLinkedAccountSweepService(_users, _crypto, _logger);

            await service.StartAsync(CancellationToken.None);

            var failure = Assert.Single(_logger.Entries);
            Assert.Contains("[Error]", failure);
            Assert.Contains("start-up sweep for password-less SSO-linked accounts failed; skipping", failure);
        }
        finally
        {
            ResetPluginInstance();
        }
    }

    [Fact]
    public void StopAsync_CompletesWithoutSideEffects()
    {
        var service = new PasswordlessLinkedAccountSweepService(_users, _crypto, _logger);

        Assert.True(service.StopAsync(CancellationToken.None).IsCompletedSuccessfully);
    }

    /// <summary>
    /// Creates the real plugin instance the service reads its links from. The configuration is lazy,
    /// so a substituted serializer keeps it off the disk.
    /// </summary>
    /// <returns>A constructed plugin whose static <c>Instance</c> is now set.</returns>
    private SSOPlugin CreatePlugin()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "sso-auth-sweep-tests-" + Guid.NewGuid().ToString("N"));
        _temporaryDirectories.Add(temporaryDirectory);
        Directory.CreateDirectory(temporaryDirectory);

        var applicationPaths = Substitute.For<IApplicationPaths>();
        applicationPaths.PluginsPath.Returns(temporaryDirectory);
        applicationPaths.PluginConfigurationsPath.Returns(temporaryDirectory);

        var serializer = Substitute.For<IXmlSerializer>();
        serializer.DeserializeFromFile(Arg.Any<Type>(), Arg.Any<string>()).Returns(new PluginConfiguration());

        return new SSOPlugin(applicationPaths, serializer);
    }

    private static void ResetPluginInstance()
    {
        typeof(SSOPlugin)
            .GetProperty(nameof(SSOPlugin.Instance), BindingFlags.Public | BindingFlags.Static)!
            .SetValue(null, null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Reset the static instance even when a test failed before its own finally block.
        ResetPluginInstance();

        foreach (var directory in _temporaryDirectories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception)
            {
                // Cleanup is best effort.
            }
        }
    }
}
