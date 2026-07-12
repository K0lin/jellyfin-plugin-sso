using Jellyfin.Plugin.SSO_Auth.Auth;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Auth;

public class OidcScopeBuilderTests
{
    [Fact]
    public void Build_UsesDefaultScopesWhenOverrideIsDisabled()
    {
        var scopes = OidcScopeBuilder.Build(null!, overrideDefaultScopes: false);

        Assert.Equal("openid profile", scopes);
    }

    [Fact]
    public void Build_AddsConfiguredScopesToDefaultsWhenOverrideIsDisabled()
    {
        var scopes = OidcScopeBuilder.Build(new[] { "groups", "user:read:email" }, overrideDefaultScopes: false);

        Assert.Equal("openid profile groups user:read:email", scopes);
    }

    [Fact]
    public void Build_UsesOnlyOpenIdWhenOverrideIsEnabledAndNoScopesAreConfigured()
    {
        var scopes = OidcScopeBuilder.Build(null!, overrideDefaultScopes: true);

        Assert.Equal("openid", scopes);
    }

    [Fact]
    public void Build_AddsConfiguredScopesWhenOverrideIsEnabled()
    {
        var scopes = OidcScopeBuilder.Build(new[] { "user:read:email" }, overrideDefaultScopes: true);

        Assert.Equal("openid user:read:email", scopes);
    }

    [Fact]
    public void Build_TrimsAndDeduplicatesConfiguredScopes()
    {
        var scopes = OidcScopeBuilder.Build(
            new[] { " openid ", "profile", "", " ", "profile", "groups", "groups" },
            overrideDefaultScopes: true);

        Assert.Equal("openid profile groups", scopes);
    }

    [Fact]
    public void Build_KeepsCaseDistinctScopes()
    {
        var scopes = OidcScopeBuilder.Build(new[] { "groups", "Groups" }, overrideDefaultScopes: true);

        Assert.Equal("openid groups Groups", scopes);
    }
}
