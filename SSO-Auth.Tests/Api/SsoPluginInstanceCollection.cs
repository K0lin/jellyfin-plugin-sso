using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.Api;

/// <summary>
/// Serializes tests that mutate <see cref="SSOPlugin.Instance"/>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SsoPluginInstanceCollection : ICollectionFixture<SsoPluginInstanceCollection.Fixture>
{
    public const string Name = "SSOPlugin.Instance";

    public sealed class Fixture
    {
    }
}
