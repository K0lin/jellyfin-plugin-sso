using Jellyfin.Plugin.SSO_Auth;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Web;

public class WebResponseCredentialRestoreTests
{
    [Fact]
    public void Generator_PreservesCredentialsOnLinkOrAuthFailure()
    {
        var html = WebResponse.Generator(
            data: "state-value",
            provider: "keycloak",
            baseUrl: "https://jellyfin.example",
            mode: "OID",
            isLinking: true);

        Assert.Contains("previousCredentials", html);
        Assert.Contains("function fail(message, previousCredentials)", html);
        Assert.Contains("localStorage.setItem('jellyfin_credentials', previousCredentials)", html);
        Assert.Contains("Login failed.", html);
        Assert.Contains("cannot link SSO account.", html);
        Assert.Contains("xhr.status >= 200 && xhr.status < 300", html);
    }

    [Fact]
    public void Generator_LoginFlowAlsoUsesFailureRestore()
    {
        var html = WebResponse.Generator(
            data: "state-value",
            provider: "keycloak",
            baseUrl: "https://jellyfin.example",
            mode: "OID",
            isLinking: false);

        Assert.Contains("const previousCredentials = localStorage.getItem('jellyfin_credentials')", html);
        Assert.Contains("fail((response && response.text) || 'Login failed.', previousCredentials)", html);
    }
}
