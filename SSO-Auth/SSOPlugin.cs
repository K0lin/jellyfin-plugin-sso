using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SSO_Auth.Config;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SSO_Auth;

/// <summary>
/// The SSO plugin class.
/// </summary>
public class SSOPlugin : BasePlugin<PluginConfiguration>, IPlugin, IHasWebPages
{
    /// <summary>
    /// The plugin name. This must stay identical to the <c>name</c> field in <c>build.yaml</c>, which is
    /// what the repository manifest and the packaged <c>meta.json</c> carry.
    /// </summary>
    /// <remarks>
    /// Jellyfin writes this value into the installed <c>meta.json</c> (<c>PluginManager.CreatePluginInstance</c>
    /// assigns <c>manifest.Name = plugin.Instance.Name</c>), and it only cleans up superseded plugin directories
    /// when the manifest names of the two directories match
    /// (<c>PluginManager.DiscoverPlugins</c> compares <c>lastName</c> against <c>entry.Name</c>).
    /// A name that differs from the manifest therefore leaves stale version directories behind, and every
    /// leftover directory adds another copy of this assembly to the ASP.NET Core application parts.
    /// </remarks>
    public const string PluginName = "SSO Authentication";

    /// <summary>
    /// The plugin GUID. This must stay identical to the <c>guid</c> field in <c>build.yaml</c>.
    /// </summary>
    public const string PluginGuid = "505ce9d1-d916-42fa-86ca-673ef241d7df";

    /// <summary>
    /// The prefix the dashboard pages of this plugin are registered under. This is part of the public
    /// URL surface (<c>web/configurationpage?name=SSO-Auth</c>) and is referenced from
    /// <c>Config/configPage.html</c>, <c>Config/config.js</c> and <c>Config/linking.html</c>, so it is
    /// deliberately kept separate from <see cref="PluginName"/>.
    /// </summary>
    public const string ConfigurationPageName = "SSO-Auth";

    /// <summary>
    /// Initializes a new instance of the <see cref="SSOPlugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Internal Jellyfin interface for the ApplicationPath.</param>
    /// <param name="xmlSerializer">Internal Jellyfin interface for the XML information.</param>
    public SSOPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the instance of the SSO plugin.
    /// </summary>
    public static SSOPlugin Instance { get; private set; }

    /// <summary>
    /// Gets the name of the SSO plugin.
    /// </summary>
    /// <remarks>
    /// The stored configuration is not affected by this name: Jellyfin derives the configuration file from the
    /// assembly file name (<c>BasePlugin{T}.ConfigurationFileName</c> is
    /// <c>Path.ChangeExtension(AssemblyFileName, ".xml")</c>), which stays <c>SSO-Auth.xml</c>.
    /// </remarks>
    public override string Name => PluginName;

    /// <summary>
    /// Gets the GUID of the SSO plugin.
    /// </summary>
    public override Guid Id => Guid.Parse(PluginGuid);

    /// <summary>
    /// Returns the available internal web pages of this plugin.
    /// </summary>
    /// <returns>A list of internal webpages in this application.</returns>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = ConfigurationPageName,
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.configPage.html"
            },
            new PluginPageInfo
            {
                Name = ConfigurationPageName + ".js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.config.js"
            },
            new PluginPageInfo
            {
                Name = ConfigurationPageName + ".css",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.style.css"
            },
            new PluginPageInfo
            {
                Name = ConfigurationPageName + "-linking",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.linking.html"
            },
            new PluginPageInfo
            {
                Name = ConfigurationPageName + "-linking.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.linking.js"
            },
            new PluginPageInfo
            {
                Name = ConfigurationPageName + "-i18n-en-us.json",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.i18n.en-us.json"
            },
            new PluginPageInfo
            {
                Name = ConfigurationPageName + "-i18n-fr.json",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.i18n.fr.json"
            },
            new PluginPageInfo
            {
                Name = ConfigurationPageName + "-i18n-sr.json",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.i18n.sr.json"
            },
        };
    }

    /// <summary>
    /// Returns the available user views for this plugin.
    /// </summary>
    /// <returns>A list of user views for this plugin.</returns>
    public IEnumerable<PluginPageInfo> GetViews()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "style.css",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.style.css"
            },
            new PluginPageInfo
            {
                Name = "linking",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.linking.html"
            },
            new PluginPageInfo
            {
                Name = "linking.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.linking.js"
            },
            new PluginPageInfo
            {
                Name = "i18n/en-us.json",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.i18n.en-us.json"
            },
            new PluginPageInfo
            {
                Name = "i18n/fr.json",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.i18n.fr.json"
            },
            new PluginPageInfo
            {
                Name = "i18n/sr.json",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.i18n.sr.json"
            },
            new PluginPageInfo
            {
                Name = "ApiClient.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.apiClient.js"
            },
            new PluginPageInfo
            {
                Name = "emby-restyle.css",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.emby-restyle.css"
            },
            new PluginPageInfo
            {
                Name = "jellyfin-apiClient.esm.min.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.jellyfin-apiClient.esm.min.js"
            },
        };
    }
}
