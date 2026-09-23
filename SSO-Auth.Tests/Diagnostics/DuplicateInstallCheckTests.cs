using System;
using System.Reflection;
using Jellyfin.Plugin.SSO_Auth.Diagnostics;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Diagnostics;

public class DuplicateInstallCheckTests
{
    [Fact]
    public void FindLoadedCopies_ReturnsOneEntryForASingleInstall()
    {
        var assemblies = new[] { typeof(SSOPlugin).Assembly, typeof(DuplicateInstallCheckTests).Assembly };

        var copies = DuplicateInstallCheck.FindLoadedCopies(assemblies);

        Assert.Equal(new[] { typeof(SSOPlugin).Assembly.Location }, copies);
    }

    [Fact]
    public void FindLoadedCopies_ReturnsEveryDistinctLocation()
    {
        var assemblies = new Assembly[]
        {
            new FakeAssembly(DuplicateInstallCheck.AssemblySimpleName, "/plugins/SSO Authentication_5.1.1/SSO-Auth.dll"),
            new FakeAssembly(DuplicateInstallCheck.AssemblySimpleName, "/plugins/SSO Authentication_5.0.0.0/SSO-Auth.dll"),
            new FakeAssembly("Duende.IdentityModel", "/plugins/SSO Authentication_5.1.1/Duende.IdentityModel.dll")
        };

        var copies = DuplicateInstallCheck.FindLoadedCopies(assemblies);

        Assert.Equal(
            new[]
            {
                "/plugins/SSO Authentication_5.0.0.0/SSO-Auth.dll",
                "/plugins/SSO Authentication_5.1.1/SSO-Auth.dll"
            },
            copies);
    }

    [Fact]
    public void FindLoadedCopies_IgnoresDynamicAssembliesWithoutALocation()
    {
        var assemblies = new Assembly[]
        {
            new FakeAssembly(DuplicateInstallCheck.AssemblySimpleName, "/plugins/SSO Authentication_5.1.1/SSO-Auth.dll"),
            new FakeAssembly(DuplicateInstallCheck.AssemblySimpleName, string.Empty)
        };

        var copies = DuplicateInstallCheck.FindLoadedCopies(assemblies);

        Assert.Equal(new[] { "/plugins/SSO Authentication_5.1.1/SSO-Auth.dll" }, copies);
    }

    [Fact]
    public void FindLoadedCopies_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => DuplicateInstallCheck.FindLoadedCopies(null!));
    }

    /// <summary>
    /// A stand-in for an assembly loaded from a plugin directory; <see cref="Assembly"/> cannot be
    /// substituted because its members are not all virtual.
    /// </summary>
    private sealed class FakeAssembly : Assembly
    {
        private readonly string _simpleName;

        public FakeAssembly(string simpleName, string location)
        {
            _simpleName = simpleName;
            Location = location;
        }

        public override string Location { get; }

        public override AssemblyName GetName() => new AssemblyName(_simpleName);
    }
}
