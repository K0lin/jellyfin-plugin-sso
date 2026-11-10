using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.SSO_Auth.Api;
using Jellyfin.Plugin.SSO_Auth.Config;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Events;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Api;

[Collection(SsoPluginInstanceCollection.Name)]
public class UserDeletedCanonicalLinkConsumerTests : IDisposable
{
    private readonly TypedCapturingLogger<UserDeletedCanonicalLinkConsumer> _logger = new();
    private readonly List<string> _temporaryDirectories = new();

    [Fact]
    public void Constructor_ThrowsWhenLoggerIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new UserDeletedCanonicalLinkConsumer(null!));
    }

    [Fact]
    public async Task OnEvent_WithoutLoadedPluginInstance_DoesNothing()
    {
        ResetPluginInstance();
        var consumer = new UserDeletedCanonicalLinkConsumer(_logger);

        await consumer.OnEvent(new UserDeletedEventArgs(TestModel.NewUser("alice")));

        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task OnEvent_RemovesCanonicalLinksForTheDeletedUserAndPersists()
    {
        ResetPluginInstance();
        var plugin = CreatePlugin();
        try
        {
            var alice = TestModel.NewUser("alice");
            var bob = TestModel.NewUser("bob");
            TestModel.Link(plugin.Configuration, "oid", "keycloak", "alice-sub", alice.Id);
            TestModel.Link(plugin.Configuration, "oid", "keycloak", "bob-sub", bob.Id);

            var consumer = new UserDeletedCanonicalLinkConsumer(_logger);
            await consumer.OnEvent(new UserDeletedEventArgs(alice));

            Assert.False(plugin.Configuration.OidConfigs["keycloak"].CanonicalLinks.ContainsKey("alice-sub"));
            Assert.Equal(bob.Id, plugin.Configuration.OidConfigs["keycloak"].CanonicalLinks["bob-sub"]);
            Assert.Contains("Removed 1 SSO canonical link(s)", Assert.Single(_logger.Entries));
        }
        finally
        {
            ResetPluginInstance();
        }
    }

    [Fact]
    public async Task OnEvent_WhenNoLinksMatch_DoesNotPersistOrLog()
    {
        ResetPluginInstance();
        var plugin = CreatePlugin();
        try
        {
            TestModel.Link(plugin.Configuration, "oid", "keycloak", "bob-sub", Guid.NewGuid());

            var consumer = new UserDeletedCanonicalLinkConsumer(_logger);
            await consumer.OnEvent(new UserDeletedEventArgs(TestModel.NewUser("alice")));

            Assert.Single(plugin.Configuration.OidConfigs["keycloak"].CanonicalLinks);
            Assert.Empty(_logger.Entries);
        }
        finally
        {
            ResetPluginInstance();
        }
    }

    [Fact]
    public void PluginServiceRegistrator_RegistersTheConsumer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new PluginServiceRegistrator().RegisterServices(services, applicationHost: null!);

        using var provider = services.BuildServiceProvider();
        var consumer = Assert.Single(provider.GetServices<IEventConsumer<UserDeletedEventArgs>>());
        Assert.IsType<UserDeletedCanonicalLinkConsumer>(consumer);
    }

    private SSOPlugin CreatePlugin()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "sso-auth-unlink-tests-" + Guid.NewGuid().ToString("N"));
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
