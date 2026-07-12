using System;
using System.Linq;
using Jellyfin.Plugin.SSO_Auth.Auth;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Auth;

public class OidcAuthorizationParameterBuilderTests
{
    [Fact]
    public void Build_ReturnsEmptyParametersWhenConfigurationIsEmpty()
    {
        var parameters = OidcAuthorizationParameterBuilder.Build(null!);

        Assert.Empty(parameters);
    }

    [Fact]
    public void Build_AcceptsScalarProviderParameters()
    {
        var parameters = OidcAuthorizationParameterBuilder.Build("{\"prompt\":\"login\",\"force_verify\":true,\"max_age\":3600}");

        Assert.Equal("login", parameters.Single(parameter => parameter.Key == "prompt").Value);
        Assert.Equal("true", parameters.Single(parameter => parameter.Key == "force_verify").Value);
        Assert.Equal("3600", parameters.Single(parameter => parameter.Key == "max_age").Value);
    }

    [Fact]
    public void Build_SerializesClaimsObject()
    {
        var parameters = OidcAuthorizationParameterBuilder.Build(
            "{\"claims\":{\"id_token\":{\"preferred_username\":null,\"picture\":null}}}");

        Assert.Equal(
            "{\"id_token\":{\"preferred_username\":null,\"picture\":null}}",
            parameters.Single(parameter => parameter.Key == "claims").Value);
    }

    [Theory]
    [InlineData("{\"scope\":\"openid\"}")]
    [InlineData("{\"STATE\":\"unsafe\"}")]
    [InlineData("{\"redirect_uri\":\"https://auth.example.invalid\"}")]
    [InlineData("{\"code_challenge\":\"unsafe\"}")]
    public void Build_RejectsReservedParameters(string configuredParameters)
    {
        Assert.Throws<ArgumentException>(() => OidcAuthorizationParameterBuilder.Build(configuredParameters));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"claims\":\"not-an-object\"}")]
    [InlineData("{\"authorization_details\":{\"type\":\"payment\"}}")]
    public void Build_RejectsInvalidParameterValues(string configuredParameters)
    {
        Assert.Throws<ArgumentException>(() => OidcAuthorizationParameterBuilder.Build(configuredParameters));
    }

    [Fact]
    public void Build_RejectsDuplicateParameters()
    {
        Assert.Throws<ArgumentException>(() => OidcAuthorizationParameterBuilder.Build("{\"prompt\":\"login\",\"prompt\":\"consent\"}"));
    }
}
