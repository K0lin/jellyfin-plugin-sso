using Jellyfin.Plugin.SSO_Auth.Auth;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Auth;

public class ContentDownloadPolicyTests
{
    [Fact]
    public void Resolve_PreservesExistingPermissionWhenPolicyIsUnchanged()
    {
        var permission = ContentDownloadPolicy.Resolve(null, isNewUser: true, applyOnEveryLogin: true);

        Assert.Null(permission);
    }

    [Fact]
    public void Resolve_AppliesConfiguredPermissionToNewUser()
    {
        var permission = ContentDownloadPolicy.Resolve(false, isNewUser: true, applyOnEveryLogin: false);

        Assert.False(permission);
    }

    [Fact]
    public void Resolve_PreservesExistingUserPermissionWhenApplyOnEveryLoginIsDisabled()
    {
        var permission = ContentDownloadPolicy.Resolve(false, isNewUser: false, applyOnEveryLogin: false);

        Assert.Null(permission);
    }

    [Fact]
    public void Resolve_AppliesConfiguredPermissionToExistingUserWhenApplyOnEveryLoginIsEnabled()
    {
        var permission = ContentDownloadPolicy.Resolve(true, isNewUser: false, applyOnEveryLogin: true);

        Assert.True(permission);
    }
}
