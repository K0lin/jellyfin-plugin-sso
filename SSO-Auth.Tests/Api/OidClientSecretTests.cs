using System;
using System.IO;
using Jellyfin.Plugin.SSO_Auth.Api;
using Jellyfin.Plugin.SSO_Auth.Config;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Api;

public class OidClientSecretTests
{
    [Fact]
    public void ResolveOidClientSecret_UsesOidSecretWhenFileUnset()
    {
        var config = new OidConfig { OidSecret = "  inline-secret  " };
        Assert.Equal("inline-secret", SSOController.ResolveOidClientSecret(config));
    }

    [Fact]
    public void ResolveOidClientSecret_PrefersFileOverOidSecret()
    {
        var path = Path.Combine(Path.GetTempPath(), "sso-oid-secret-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(path, "  file-secret  \n");
            var config = new OidConfig
            {
                OidSecret = "inline-secret",
                OidSecretFile = path,
            };
            Assert.Equal("file-secret", SSOController.ResolveOidClientSecret(config));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResolveOidClientSecret_ReadsFileOnEachCall()
    {
        var path = Path.Combine(Path.GetTempPath(), "sso-oid-secret-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(path, "first");
            var config = new OidConfig { OidSecretFile = path };
            Assert.Equal("first", SSOController.ResolveOidClientSecret(config));

            File.WriteAllText(path, "rotated");
            Assert.Equal("rotated", SSOController.ResolveOidClientSecret(config));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResolveOidClientSecret_MissingFile_ThrowsInvalidOperationException()
    {
        var path = Path.Combine(Path.GetTempPath(), "sso-oid-secret-missing-" + Guid.NewGuid().ToString("N"));
        var config = new OidConfig { OidSecretFile = path };

        var ex = Assert.Throws<InvalidOperationException>(() => SSOController.ResolveOidClientSecret(config));
        Assert.Contains(path, ex.Message);
        Assert.IsAssignableFrom<IOException>(ex.InnerException);
    }
}
