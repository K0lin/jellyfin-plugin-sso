using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests;

public class WebResponseTests
{
    [Fact]
    public void Generator_PrefersNativeShellDeviceIdOverLocalStorage()
    {
        var html = WebResponse.Generator("state", "provider", "https://jellyfin.example.invalid", "OID");

        Assert.Contains("contentWindow.NativeShell?.AppHost?.deviceId?.()", html);
        Assert.Contains("localStorage.getItem(\"_deviceId2\") == null && nativeDeviceId() == null", html);
        Assert.Contains("var deviceId = nativeDeviceId() ?? localStorage.getItem(\"_deviceId2\");", html);
    }
}
