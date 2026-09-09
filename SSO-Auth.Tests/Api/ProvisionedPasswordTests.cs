using System;
using Jellyfin.Plugin.SSO_Auth.Api;
using MediaBrowser.Model.Cryptography;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Api;

public class ProvisionedPasswordTests
{
    [Fact]
    public void Mint_ThrowsWhenCryptoProviderIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => ProvisionedPassword.Mint(null!));
    }

    [Fact]
    public void Mint_ReturnsTheCryptoProvidersHashedString()
    {
        var crypto = new RecordingCryptoProvider();

        var minted = ProvisionedPassword.Mint(crypto);

        var hashedInput = Assert.Single(crypto.HashedPasswords);
        Assert.Equal(RecordingCryptoProvider.HashOf(hashedInput), minted);
    }

    [Fact]
    public void Mint_HashesSixtyFourBytesOfEntropy()
    {
        var crypto = new RecordingCryptoProvider();

        ProvisionedPassword.Mint(crypto);

        // 64 bytes of CSPRNG output encode to 88 base64 characters.
        var hashedInput = Assert.Single(crypto.HashedPasswords);
        Assert.Equal(64, Convert.FromBase64String(hashedInput).Length);
    }

    [Fact]
    public void Mint_NeverRepeatsEntropy()
    {
        var crypto = new RecordingCryptoProvider();

        ProvisionedPassword.Mint(crypto);
        ProvisionedPassword.Mint(crypto);

        Assert.Equal(2, crypto.HashedPasswords.Count);
        Assert.NotEqual(crypto.HashedPasswords[0], crypto.HashedPasswords[1]);
    }
}
