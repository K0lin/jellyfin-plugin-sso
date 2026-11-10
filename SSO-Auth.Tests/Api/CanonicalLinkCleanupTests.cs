using System;
using Jellyfin.Plugin.SSO_Auth.Api;
using Jellyfin.Plugin.SSO_Auth.Config;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Api;

public class CanonicalLinkCleanupTests
{
    [Fact]
    public void RemoveLinksToUser_ThrowsWhenConfigurationIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => CanonicalLinkCleanup.RemoveLinksToUser(null!, Guid.NewGuid()));
    }

    [Fact]
    public void RemoveLinksToUser_RemovesMatchingOidAndSamlEntriesAndLeavesOthers()
    {
        var deleted = Guid.NewGuid();
        var surviving = Guid.NewGuid();
        var configuration = new PluginConfiguration();
        TestModel.Link(configuration, "oid", "keycloak", "deleted-sub", deleted);
        TestModel.Link(configuration, "oid", "keycloak", "other-sub", surviving);
        TestModel.Link(configuration, "saml", "idp", "deleted-nameid", deleted);
        TestModel.Link(configuration, "saml", "idp", "other-nameid", surviving);

        var removed = CanonicalLinkCleanup.RemoveLinksToUser(configuration, deleted);

        Assert.Equal(2, removed);
        Assert.False(configuration.OidConfigs["keycloak"].CanonicalLinks.ContainsKey("deleted-sub"));
        Assert.Equal(surviving, configuration.OidConfigs["keycloak"].CanonicalLinks["other-sub"]);
        Assert.False(configuration.SamlConfigs["idp"].CanonicalLinks.ContainsKey("deleted-nameid"));
        Assert.Equal(surviving, configuration.SamlConfigs["idp"].CanonicalLinks["other-nameid"]);
    }

    [Fact]
    public void RemoveLinksToUser_ReturnsZeroWhenNoLinksPointAtTheUser()
    {
        var configuration = new PluginConfiguration();
        TestModel.Link(configuration, "oid", "keycloak", "alice", Guid.NewGuid());

        var removed = CanonicalLinkCleanup.RemoveLinksToUser(configuration, Guid.NewGuid());

        Assert.Equal(0, removed);
        Assert.Single(configuration.OidConfigs["keycloak"].CanonicalLinks);
    }

    [Fact]
    public void RemoveLinksToUser_RemovesTheSameUserAcrossMultipleProviders()
    {
        var deleted = Guid.NewGuid();
        var configuration = new PluginConfiguration();
        TestModel.Link(configuration, "oid", "keycloak", "sub-a", deleted);
        TestModel.Link(configuration, "oid", "authentik", "sub-b", deleted);

        var removed = CanonicalLinkCleanup.RemoveLinksToUser(configuration, deleted);

        Assert.Equal(2, removed);
        Assert.Empty(configuration.OidConfigs["keycloak"].CanonicalLinks);
        Assert.Empty(configuration.OidConfigs["authentik"].CanonicalLinks);
    }
}
