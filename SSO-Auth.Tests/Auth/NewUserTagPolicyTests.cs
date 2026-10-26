using Jellyfin.Plugin.SSO_Auth.Auth;
using MediaBrowser.Model.Users;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Auth;

public class NewUserTagPolicyTests
{
    [Fact]
    public void Apply_SetsConfiguredTagsOnNewUser()
    {
        var policy = new UserPolicy();

        var applied = NewUserTagPolicy.Apply(policy, new[] { " streaming ", "", "Streaming", "family" }, isNewUser: true);

        Assert.True(applied);
        Assert.Equal(new[] { "streaming", "family" }, policy.AllowedTags);
    }

    [Fact]
    public void Apply_LeavesExistingUserTagsUntouchedAtLogin()
    {
        var policy = new UserPolicy { AllowedTags = new[] { "set-by-an-admin" } };

        var applied = NewUserTagPolicy.Apply(policy, new[] { "streaming" }, isNewUser: false);

        Assert.False(applied);
        Assert.Equal(new[] { "set-by-an-admin" }, policy.AllowedTags);
    }

    [Fact]
    public void Apply_EmptyListLeavesNewUserTagsEmpty()
    {
        var policy = new UserPolicy();

        Assert.False(NewUserTagPolicy.Apply(policy, new string[0], isNewUser: true));
        Assert.False(NewUserTagPolicy.Apply(policy, new[] { " " }, isNewUser: true));
        Assert.False(NewUserTagPolicy.Apply(policy, null!, isNewUser: true));
        Assert.Empty(policy.AllowedTags);
    }
}
