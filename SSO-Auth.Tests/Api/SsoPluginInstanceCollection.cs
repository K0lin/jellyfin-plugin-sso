using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Api;

/// <summary>
/// Marker fixture for tests that mutate the static <see cref="SSOPlugin.Instance"/>.
/// </summary>
public sealed class SsoPluginInstanceFixture
{
}

/// <summary>
/// Serializes tests that mutate the static <see cref="SSOPlugin.Instance"/>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SsoPluginInstanceCollection : ICollectionFixture<SsoPluginInstanceFixture>
{
    /// <summary>
    /// The collection name shared by tests that own <see cref="SSOPlugin.Instance"/>.
    /// </summary>
    public const string Name = "SSOPlugin.Instance";
}
