using System;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SSO_Auth.Api;
using MediaBrowser.Controller.Library;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Api;

public class PasswordlessAccountSealerTests
{
    private readonly IUserManager _users = Substitute.For<IUserManager>();
    private readonly RecordingCryptoProvider _crypto = new();
    private readonly CapturingLogger _logger = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SealAsync_SealsAnAccountWithoutAStoredPassword(string? storedPassword)
    {
        var alice = TestModel.NewUser("alice", storedPassword);

        var sealedNow = await PasswordlessAccountSealer.SealAsync(alice, _users, _crypto);

        Assert.True(sealedNow);
        Assert.Equal(RecordingCryptoProvider.HashOf(_crypto.HashedPasswords[0]), alice.Password);
        Assert.Equal(TestModel.DefaultProvider, alice.AuthenticationProviderId);
        await _users.Received(1).UpdateUserAsync(alice);
    }

    [Fact]
    public async Task SealAsync_KeepsAnExistingPassword()
    {
        var alice = TestModel.NewUser("alice", "$PBKDF2$AB");

        var sealedNow = await PasswordlessAccountSealer.SealAsync(alice, _users, _crypto);

        Assert.False(sealedNow);
        Assert.Equal("$PBKDF2$AB", alice.Password);
        await _users.DidNotReceiveWithAnyArgs().UpdateUserAsync(Arg.Any<User>());
        Assert.Empty(_crypto.HashedPasswords);
    }

    [Fact]
    public async Task SealAsync_ThrowsWhenAnyDependencyIsNull()
    {
        var alice = TestModel.NewUser("alice");

        await Assert.ThrowsAsync<ArgumentNullException>(() => PasswordlessAccountSealer.SealAsync(null!, _users, _crypto));
        await Assert.ThrowsAsync<ArgumentNullException>(() => PasswordlessAccountSealer.SealAsync(alice, null!, _crypto));
        await Assert.ThrowsAsync<ArgumentNullException>(() => PasswordlessAccountSealer.SealAsync(alice, _users, null!));
    }

    [Fact]
    public void PasswordlessAccountSealedAtLogin_WhenWarningLevelIsDisabled_WritesNothing()
    {
        SsoAudit.PasswordlessAccountSealedAtLogin(new SilentLogger());
    }

    [Fact]
    public async Task SealNewAccountAsync_MintsAndPersistsWithoutDeleting()
    {
        var alice = TestModel.NewUser("alice");

        await PasswordlessAccountSealer.SealNewAccountAsync(alice, _users, _crypto, _logger);

        Assert.Equal(RecordingCryptoProvider.HashOf(_crypto.HashedPasswords[0]), alice.Password);
        await _users.Received(1).UpdateUserAsync(alice);
        await _users.DidNotReceiveWithAnyArgs().DeleteUserAsync(Arg.Any<Guid>());
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task SealNewAccountAsync_RemovesTheAccountAgainWhenThePersistFails()
    {
        var alice = TestModel.NewUser("alice");
        _users
            .When(userManager => userManager.UpdateUserAsync(alice))
            .Throw(new InvalidOperationException("persist failed"));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PasswordlessAccountSealer.SealNewAccountAsync(alice, _users, _crypto, _logger));

        Assert.Equal("persist failed", thrown.Message);
        await _users.Received(1).DeleteUserAsync(alice.Id);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task SealNewAccountAsync_WhenTheDeleteAlsoFails_LogsTheAccountAndRethrows()
    {
        var alice = TestModel.NewUser("alice");
        var persistFailure = new InvalidOperationException("persist failed");
        _users
            .When(userManager => userManager.UpdateUserAsync(alice))
            .Throw(persistFailure);
        _users
            .When(userManager => userManager.DeleteUserAsync(alice.Id))
            .Throw(new InvalidOperationException("delete failed too"));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PasswordlessAccountSealer.SealNewAccountAsync(alice, _users, _crypto, _logger));

        Assert.Same(persistFailure, thrown);
        var failure = Assert.Single(_logger.Entries);
        Assert.Contains("[Error]", failure);
        Assert.Contains("alice", failure);
        Assert.Contains("accepts an empty password until one is set", failure);
    }
}
