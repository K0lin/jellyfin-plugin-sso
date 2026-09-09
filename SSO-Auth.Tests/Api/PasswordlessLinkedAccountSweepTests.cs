using System;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SSO_Auth.Api;
using Jellyfin.Plugin.SSO_Auth.Config;
using MediaBrowser.Controller.Library;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Api;

public class PasswordlessLinkedAccountSweepTests
{
    private readonly IUserManager _users = Substitute.For<IUserManager>();
    private readonly RecordingCryptoProvider _crypto = new();
    private readonly CapturingLogger _logger = new();

    [Fact]
    public async Task SweepAsync_SealsPasswordlessLinkedAccountAndAuditsOnce()
    {
        var alice = TestModel.NewUser("alice");
        var configuration = new PluginConfiguration();
        TestModel.Link(configuration, "oid", "keycloak", "alice", alice.Id);
        _users.GetUserById(alice.Id).Returns(alice);

        var sealedCount = await new PasswordlessLinkedAccountSweep(configuration, _users, _crypto, _logger).SweepAsync();

        Assert.Equal(1, sealedCount);
        Assert.Equal(RecordingCryptoProvider.HashOf(_crypto.HashedPasswords[0]), alice.Password);
        await _users.Received(1).UpdateUserAsync(alice);

        var audit = Assert.Single(_logger.Entries);
        Assert.Contains("[Warning]", audit);
        Assert.Contains("[SSO Audit] Sealed 1 SSO-linked account(s) that had no stored password", audit);
    }

    [Fact]
    public async Task SweepAsync_LeavesAccountThatAlreadyHoldsAPasswordAlone()
    {
        var alice = TestModel.NewUser("alice", "$PBKDF2$AB");
        var configuration = new PluginConfiguration();
        TestModel.Link(configuration, "oid", "keycloak", "alice", alice.Id);
        _users.GetUserById(alice.Id).Returns(alice);

        var sealedCount = await new PasswordlessLinkedAccountSweep(configuration, _users, _crypto, _logger).SweepAsync();

        Assert.Equal(0, sealedCount);
        Assert.Equal("$PBKDF2$AB", alice.Password);
        await _users.DidNotReceiveWithAnyArgs().UpdateUserAsync(Arg.Any<User>());
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task SweepAsync_TreatsEmptyStoredPasswordAsPasswordless()
    {
        var alice = TestModel.NewUser("alice", password: string.Empty);
        var configuration = new PluginConfiguration();
        TestModel.Link(configuration, "saml", "keycloak", "alice", alice.Id);
        _users.GetUserById(alice.Id).Returns(alice);

        var sealedCount = await new PasswordlessLinkedAccountSweep(configuration, _users, _crypto, _logger).SweepAsync();

        Assert.Equal(1, sealedCount);
        Assert.NotNull(alice.Password);
    }

    [Fact]
    public async Task SweepAsync_LeavesLoginRoutingUntouched()
    {
        var alice = TestModel.NewUser("alice", provider: "Some.Custom.AuthenticationProvider");
        var configuration = new PluginConfiguration();
        TestModel.Link(configuration, "oid", "keycloak", "alice", alice.Id);
        _users.GetUserById(alice.Id).Returns(alice);

        await new PasswordlessLinkedAccountSweep(configuration, _users, _crypto, _logger).SweepAsync();

        Assert.Equal("Some.Custom.AuthenticationProvider", alice.AuthenticationProviderId);
    }

    [Fact]
    public async Task SweepAsync_SkipsLinkWhoseAccountNoLongerExists()
    {
        var configuration = new PluginConfiguration();
        TestModel.Link(configuration, "oid", "keycloak", "alice", Guid.NewGuid());
        _users.GetUserById(Arg.Any<Guid>()).Returns((User?)null);

        var sealedCount = await new PasswordlessLinkedAccountSweep(configuration, _users, _crypto, _logger).SweepAsync();

        Assert.Equal(0, sealedCount);
        await _users.DidNotReceiveWithAnyArgs().UpdateUserAsync(Arg.Any<User>());
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task SweepAsync_SealsEachDistinctAccountOnceAcrossProvidersAndModes()
    {
        var alice = TestModel.NewUser("alice");
        var bob = TestModel.NewUser("bob");
        var configuration = new PluginConfiguration();
        TestModel.Link(configuration, "oid", "keycloak", "alice", alice.Id);
        TestModel.Link(configuration, "saml", "keycloak", "alice@idp", alice.Id);
        TestModel.Link(configuration, "oid", "authentik", "bob", bob.Id);
        _users.GetUserById(alice.Id).Returns(alice);
        _users.GetUserById(bob.Id).Returns(bob);

        var sealedCount = await new PasswordlessLinkedAccountSweep(configuration, _users, _crypto, _logger).SweepAsync();

        Assert.Equal(2, sealedCount);
        await _users.Received(1).UpdateUserAsync(alice);
        await _users.Received(1).UpdateUserAsync(bob);
        Assert.Equal(2, _crypto.HashedPasswords.Count);
        Assert.Contains("Sealed 2 SSO-linked account(s)", Assert.Single(_logger.Entries));
    }

    [Fact]
    public async Task SweepAsync_IgnoresProvidersThatHoldNoLinks()
    {
        var configuration = new PluginConfiguration();
        configuration.OidConfigs["keycloak"] = new OidConfig();

        var sealedCount = await new PasswordlessLinkedAccountSweep(configuration, _users, _crypto, _logger).SweepAsync();

        Assert.Equal(0, sealedCount);
        _users.DidNotReceiveWithAnyArgs().GetUserById(Arg.Any<Guid>());
    }

    [Fact]
    public async Task SweepAsync_IsIdempotentAcrossPasses()
    {
        var alice = TestModel.NewUser("alice");
        var configuration = new PluginConfiguration();
        TestModel.Link(configuration, "oid", "keycloak", "alice", alice.Id);
        _users.GetUserById(alice.Id).Returns(alice);
        var sweep = new PasswordlessLinkedAccountSweep(configuration, _users, _crypto, _logger);

        var firstPass = await sweep.SweepAsync();
        var secondPass = await sweep.SweepAsync();

        Assert.Equal(1, firstPass);
        Assert.Equal(0, secondPass);
        await _users.Received(1).UpdateUserAsync(alice);
        Assert.Single(_logger.Entries);
    }

    [Fact]
    public void PasswordlessAccountsSealed_WhenWarningLevelIsDisabled_WritesNothing()
    {
        SsoAudit.PasswordlessAccountsSealed(new SilentLogger(), 3);
    }

    [Fact]
    public void Constructor_ThrowsWhenAnyDependencyIsNull()
    {
        var configuration = new PluginConfiguration();

        Assert.Throws<ArgumentNullException>(() => new PasswordlessLinkedAccountSweep(null!, _users, _crypto, _logger));
        Assert.Throws<ArgumentNullException>(() => new PasswordlessLinkedAccountSweep(configuration, null!, _crypto, _logger));
        Assert.Throws<ArgumentNullException>(() => new PasswordlessLinkedAccountSweep(configuration, _users, null!, _logger));
        Assert.Throws<ArgumentNullException>(() => new PasswordlessLinkedAccountSweep(configuration, _users, _crypto, null!));
    }
}
