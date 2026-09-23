using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests;

/// <summary>
/// Guards the identity the plugin reports against the identity it is packaged with.
/// </summary>
/// <remarks>
/// Jellyfin copies <see cref="SSOPlugin.Name"/> into the installed <c>meta.json</c> and only removes a
/// superseded plugin directory when the manifest names of both directories match. A drift between
/// <c>build.yaml</c> and the plugin class therefore leaves stale directories behind, which loads this
/// assembly twice and breaks every route of the plugin with HTTP 500 (issue #59).
/// </remarks>
public class PluginManifestConsistencyTests
{
    private static readonly IReadOnlyDictionary<string, string> BuildManifest = ReadBuildManifest();

    [Fact]
    public void PluginName_MatchesBuildManifest()
    {
        Assert.Equal(SSOPlugin.PluginName, BuildManifest["name"]);
    }

    [Fact]
    public void PluginGuid_MatchesBuildManifest()
    {
        Assert.Equal(Guid.Parse(BuildManifest["guid"]), Guid.Parse(SSOPlugin.PluginGuid));
    }

    [Fact]
    public void AssemblyVersion_MatchesBuildManifest()
    {
        var expected = Version.Parse(BuildManifest["version"]);
        var actual = typeof(SSOPlugin).Assembly.GetName().Version;

        Assert.NotNull(actual);

        // The manifest version may have three segments; an assembly version always has four.
        Assert.Equal(expected.Major, actual!.Major);
        Assert.Equal(expected.Minor, actual.Minor);
        Assert.Equal(Math.Max(expected.Build, 0), actual.Build);
        Assert.Equal(Math.Max(expected.Revision, 0), actual.Revision);
    }

    /// <summary>
    /// The dashboard page names are a public URL surface referenced from the shipped HTML and JS, so they
    /// must not follow a change of the plugin name.
    /// </summary>
    [Fact]
    public void ConfigurationPageName_IsStable()
    {
        Assert.Equal("SSO-Auth", SSOPlugin.ConfigurationPageName);
    }

    private static IReadOnlyDictionary<string, string> ReadBuildManifest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "build.yaml");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(path))
        {
            // Only the top-level scalar keys are needed, so a full YAML parser would be overkill here.
            if (line.Length == 0 || char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"');
            if (value.Length > 0 && !values.ContainsKey(key))
            {
                values[key] = value;
            }
        }

        foreach (var required in new[] { "name", "guid", "version" })
        {
            Assert.True(values.ContainsKey(required), $"build.yaml is missing the '{required}' field.");
        }

        return values;
    }
}
