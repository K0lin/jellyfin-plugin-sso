using System.Security.Claims;
using Jellyfin.Plugin.SSO_Auth.Auth;
using Jellyfin.Plugin.SSO_Auth.Config;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Auth;

public class AuthorizationEvaluatorTests
{
    [Fact]
    public void ResolveOidcUsername_ReturnsPreferredUsernameByDefault()
    {
        var username = AuthorizationEvaluator.ResolveOidcUsername(
            new[]
            {
                new Claim("preferred_username", "jellyfin-user"),
                new Claim("sub", "subject-id")
            },
            null!);

        Assert.Equal("jellyfin-user", username);
    }

    [Fact]
    public void ResolveOidcUsername_ReturnsConfiguredUsernameClaim()
    {
        var username = AuthorizationEvaluator.ResolveOidcUsername(
            new[]
            {
                new Claim("email", "user@example.invalid"),
                new Claim("sub", "subject-id")
            },
            " email ");

        Assert.Equal("user@example.invalid", username);
    }

    [Fact]
    public void ResolveOidcUsername_FallsBackToSubjectWhenPreferredClaimIsMissing()
    {
        var username = AuthorizationEvaluator.ResolveOidcUsername(
            new[] { new Claim("sub", "subject-id") },
            null!);

        Assert.Equal("subject-id", username);
    }

    [Fact]
    public void ResolveOidcUsername_ReturnsNullWhenNoUsableClaimExists()
    {
        var username = AuthorizationEvaluator.ResolveOidcUsername(
            new[] { new Claim("other", "value") },
            null!);

        Assert.Null(username);
    }

    [Fact]
    public void ExtractOidcRoles_ReturnsSimpleClaimValue()
    {
        var roles = AuthorizationEvaluator.ExtractOidcRoles(
            new[] { new Claim("roles", "admin") },
            "roles");

        Assert.Equal(new[] { "admin" }, roles);
    }

    [Fact]
    public void ExtractOidcRoles_ReturnsJsonArrayRoles()
    {
        var roles = AuthorizationEvaluator.ExtractOidcRoles(
            new[] { new Claim("realm_access", "{\"roles\":[\"admin\",\"user\"]}") },
            "realm_access.roles");

        Assert.Equal(new[] { "admin", "user" }, roles);
    }

    [Fact]
    public void ExtractOidcRoles_SupportsEscapedDotsInJsonPath()
    {
        var roles = AuthorizationEvaluator.ExtractOidcRoles(
            new[] { new Claim("resource_access", "{\"jellyfin.client\":{\"roles\":[\"media-admin\"]}}") },
            "resource_access.jellyfin\\.client.roles");

        Assert.Equal(new[] { "media-admin" }, roles);
    }

    [Fact]
    public void ExtractOidcRoles_ReturnsEmptyWhenClaimIsMissing()
    {
        var roles = AuthorizationEvaluator.ExtractOidcRoles(
            new[] { new Claim("other", "admin") },
            "roles");

        Assert.Empty(roles);
    }

    [Fact]
    public void ExtractOidcRoles_ReturnsEmptyWhenJsonPathIsMissing()
    {
        var roles = AuthorizationEvaluator.ExtractOidcRoles(
            new[] { new Claim("realm_access", "{\"groups\":[\"admin\"]}") },
            "realm_access.roles");

        Assert.Empty(roles);
    }

    [Fact]
    public void ExtractOidcRoles_ReturnsEmptyWhenJsonArrayIsEmpty()
    {
        var roles = AuthorizationEvaluator.ExtractOidcRoles(
            new[] { new Claim("realm_access", "{\"roles\":[]}") },
            "realm_access.roles");

        Assert.Empty(roles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ExtractOidcRoles_ReturnsEmptyWhenRoleClaimIsMissing(string? roleClaim)
    {
        var roles = AuthorizationEvaluator.ExtractOidcRoles(
            new[] { new Claim("roles", "admin") },
            roleClaim);

        Assert.Empty(roles);
    }

    [Fact]
    public void Evaluate_AllowsAuthenticationWhenAllowedRolesAreNull()
    {
        var result = Evaluate(roles: Array.Empty<string>(), allowedRoles: null);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Evaluate_AllowsAuthenticationWhenAllowedRolesAreEmpty()
    {
        var result = Evaluate(roles: Array.Empty<string>(), allowedRoles: Array.Empty<string>());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Evaluate_AllowsAuthenticationOnExactRoleMatch()
    {
        var result = Evaluate(roles: new[] { "users" }, allowedRoles: new[] { "users" });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Evaluate_RoleMatchingIsCaseSensitive()
    {
        var result = Evaluate(roles: new[] { "Users" }, allowedRoles: new[] { "users" });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Evaluate_IgnoresNullProviderRoles()
    {
        var result = Evaluate(roles: new[] { null!, "users" }, allowedRoles: new[] { "users" });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Evaluate_GrantsAdminOnExactAdminRoleMatch()
    {
        var result = Evaluate(roles: new[] { "admin" }, adminRoles: new[] { "admin" });

        Assert.True(result.IsAdmin);
    }

    [Fact]
    public void Evaluate_DoesNotGrantAdminForNonAdminRole()
    {
        var result = Evaluate(roles: new[] { "user" }, adminRoles: new[] { "admin" });

        Assert.False(result.IsAdmin);
    }

    [Fact]
    public void Evaluate_UsesDefaultFoldersWhenFolderRolesAreDisabled()
    {
        var result = Evaluate(
            roles: Array.Empty<string>(),
            defaultFolders: new[] { "folder-a", "folder-b" },
            enableFolderRoles: false);

        Assert.Equal(new[] { "folder-a", "folder-b" }, result.Folders);
    }

    [Fact]
    public void Evaluate_AddsFoldersFromMatchedFolderRole()
    {
        var result = Evaluate(
            roles: new[] { "media" },
            enableFolderRoles: true,
            folderRoleMapping: new[]
            {
                new FolderRoleMap { Role = "media", Folders = new List<string> { "folder-a", "folder-b" } }
            });

        Assert.Equal(new[] { "folder-a", "folder-b" }, result.Folders);
    }

    [Fact]
    public void Evaluate_DoesNotAddMappedFoldersWhenFolderRolesAreDisabled()
    {
        var result = Evaluate(
            roles: new[] { "media" },
            defaultFolders: Array.Empty<string>(),
            enableFolderRoles: false,
            folderRoleMapping: new[]
            {
                new FolderRoleMap { Role = "media", Folders = new List<string> { "folder-a" } }
            });

        Assert.Empty(result.Folders);
    }

    [Fact]
    public void Evaluate_EnablesLiveTvFromMatchedRole()
    {
        var result = Evaluate(
            roles: new[] { "tv" },
            enableLiveTvRoles: true,
            liveTvRoles: new[] { "tv" });

        Assert.True(result.EnableLiveTv);
    }

    [Fact]
    public void Evaluate_EnablesLiveTvManagementFromMatchedRole()
    {
        var result = Evaluate(
            roles: new[] { "tv-admin" },
            enableLiveTvRoles: true,
            liveTvManagementRoles: new[] { "tv-admin" });

        Assert.True(result.EnableLiveTvManagement);
    }

    [Fact]
    public void Evaluate_PreservesDefaultLiveTvValues()
    {
        var result = Evaluate(
            roles: Array.Empty<string>(),
            defaultLiveTv: true,
            defaultLiveTvManagement: true);

        Assert.True(result.EnableLiveTv);
        Assert.True(result.EnableLiveTvManagement);
    }

    private static AuthorizationEvaluation Evaluate(
        IEnumerable<string> roles,
        IReadOnlyCollection<string>? allowedRoles = null,
        IReadOnlyCollection<string>? adminRoles = null,
        IEnumerable<string>? defaultFolders = null,
        bool enableFolderRoles = false,
        IEnumerable<FolderRoleMap>? folderRoleMapping = null,
        bool defaultLiveTv = false,
        bool defaultLiveTvManagement = false,
        bool enableLiveTvRoles = false,
        IReadOnlyCollection<string>? liveTvRoles = null,
        IReadOnlyCollection<string>? liveTvManagementRoles = null)
    {
        return AuthorizationEvaluator.Evaluate(
            roles,
            allowedRoles,
            adminRoles,
            defaultFolders,
            enableFolderRoles,
            folderRoleMapping,
            defaultLiveTv,
            defaultLiveTvManagement,
            enableLiveTvRoles,
            liveTvRoles,
            liveTvManagementRoles);
    }
}
