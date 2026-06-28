using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Duende.IdentityModel.OidcClient;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.SSO_Auth.Auth;
using Jellyfin.Plugin.SSO_Auth.Config;
using Jellyfin.Plugin.SSO_Auth.Helpers;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO_Auth.Api;

/// <summary>
/// The sso api controller.
/// </summary>
[ApiController]
[Route("[controller]")]
public class SSOController : ControllerBase
{
    private readonly IUserManager _userManager;
    private readonly ISessionManager _sessionManager;
    private readonly IAuthorizationContext _authContext;
    private readonly ILogger<SSOController> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ICryptoProvider _cryptoProvider;
    private readonly IProviderManager _providerManager;
    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly ConcurrentDictionary<string, TimedAuthorizeState> StateManager = new ConcurrentDictionary<string, TimedAuthorizeState>();

    /// <summary>
    /// Initializes a new instance of the <see cref="SSOController"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{SSOController}"/> interface.</param>
    /// <param name="loggerFactory">Instance of the <see cref="ILoggerFactory"/> interface.</param>
    /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
    /// <param name="authContext">Instance of the <see cref="IAuthorizationContext"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="cryptoProvider">Instance of the <see cref="ICryptoProvider"/> interface.</param>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    public SSOController(
        ILogger<SSOController> logger,
        ILoggerFactory loggerFactory,
        ISessionManager sessionManager,
        IUserManager userManager,
        IAuthorizationContext authContext,
        ICryptoProvider cryptoProvider,
        IProviderManager providerManager,
        IHttpClientFactory httpClientFactory,
        IServerConfigurationManager serverConfigurationManager)
    {
        _sessionManager = sessionManager;
        _userManager = userManager;
        _authContext = authContext;
        _cryptoProvider = cryptoProvider;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _providerManager = providerManager;
        _serverConfigurationManager = serverConfigurationManager;
        _httpClientFactory = httpClientFactory;
        _logger.LogInformation("SSO Controller initialized");
    }

    /// <summary>
    /// The GET endpoint for OpenID provider to callback to. Returns a webpage that parses client data and completes auth.
    /// </summary>
    /// <param name="provider">The ID of the provider which will use the callback information.</param>
    /// <param name="state">The current request state.</param>
    /// <returns>A webpage that will complete the client-side flow.</returns>
    // Actually a GET: https://github.com/IdentityModel/IdentityModel.OidcClient/issues/325
    [HttpGet("OID/r/{provider}")]
    [HttpGet("OID/redirect/{provider}")]
    public async Task<ActionResult> OidPost(
        [FromRoute] string provider,
        [FromQuery] string state) // Although this is a GET function, this function is called `Post` for consistency with SAML
    {
        OidConfig config;
        try
        {
            config = SSOPlugin.Instance.Configuration.OidConfigs[provider];
        }
        catch (KeyNotFoundException)
        {
            return BadRequest("No matching provider found");
        }

        if (config.Enabled)
        {
            if (string.IsNullOrEmpty(state))
            {
                return BadRequest("Missing state");
            }

            if (!StateManager.TryGetValue(state, out var timedState))
            {
                return BadRequest("Invalid or expired state");
            }

            var scopes = config.OidScopes == null ? new string[2] : config.OidScopes;
            var options = new OidcClientOptions
            {
                Authority = config.OidEndpoint?.Trim(),
                ClientId = config.OidClientId?.Trim(),
                ClientSecret = config.OidSecret?.Trim(),
                RedirectUri = GetRequestBase(config.SchemeOverride, config.PortOverride) + $"/sso/OID/{(Request.Path.Value.Contains("/start/", StringComparison.InvariantCultureIgnoreCase) ? "redirect" : "r")}/" + provider,
                Scope = string.Join(" ", scopes.Prepend("openid profile")),
                DisablePushedAuthorization = config.DisablePushedAuthorization,
                LoggerFactory = _loggerFactory,
                LoadProfile = !config.DoNotLoadProfile,
                HttpClientFactory = o =>
                {
                    var client = _httpClientFactory.CreateClient();
                    System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
                    string version = fvi.FileVersion;
                    client.DefaultRequestHeaders.UserAgent.ParseAdd($"Jellyfin-Plugin-SSO-Auth +{version} (https://github.com/k0lin/jellyfin-plugin-sso)");
                    return client;
                }
            };
            var oidEndpointUri = new Uri(config.OidEndpoint?.Trim());
            options.Policy.Discovery.AdditionalEndpointBaseAddresses.Add(oidEndpointUri.GetLeftPart(UriPartial.Authority));
            options.Policy.Discovery.ValidateEndpoints = !config.DoNotValidateEndpoints; // For Google and other providers with different endpoints
            options.Policy.Discovery.RequireHttps = !config.DisableHttps;
            options.Policy.Discovery.ValidateIssuerName = !config.DoNotValidateIssuerName;
            var oidcClient = new OidcClient(options);
            var currentState = timedState.State;
            var result = await oidcClient.ProcessResponseAsync(Request.QueryString.Value, currentState).ConfigureAwait(false);

            if (result.IsError)
            {
                return ReturnError(StatusCodes.Status400BadRequest, $"Error logging in: {result.Error} - {result.ErrorDescription}");
            }

            var defaultFolders = !config.EnableFolderRoles && config.EnabledFolders != null ? config.EnabledFolders : Array.Empty<string>();
            timedState.Folders = new List<string>(defaultFolders);

            timedState.EnableLiveTv = config.EnableLiveTv;
            timedState.EnableLiveTvManagement = config.EnableLiveTvManagement;

            if (config.AvatarUrlFormat is not null)
            {
                timedState.AvatarURL = result.User.Claims.Aggregate(
                    config.AvatarUrlFormat,
                    (s, claim) => s.Contains($"@{{{claim.Type}}}") ? s.Replace($"@{{{claim.Type}}}", claim.Value) : s);
            }

            timedState.Username = AuthorizationEvaluator.ResolveOidcUsername(result.User.Claims, config.DefaultUsernameClaim);

            var oidcRoles = AuthorizationEvaluator.ExtractOidcRoles(result.User.Claims, config.RoleClaim);
            var oidcAuthorization = AuthorizationEvaluator.Evaluate(
                oidcRoles,
                config.Roles,
                config.AdminRoles,
                defaultFolders,
                config.EnableFolderRoles,
                config.FolderRoleMapping,
                timedState.EnableLiveTv,
                timedState.EnableLiveTvManagement,
                config.EnableLiveTvRoles,
                config.LiveTvRoles,
                config.LiveTvManagementRoles);

            timedState.Valid = !string.IsNullOrEmpty(timedState.Username) && oidcAuthorization.IsValid;
            timedState.Admin = oidcAuthorization.IsAdmin;
            timedState.Folders = new List<string>(oidcAuthorization.Folders);
            timedState.EnableLiveTv = oidcAuthorization.EnableLiveTv;
            timedState.EnableLiveTvManagement = oidcAuthorization.EnableLiveTvManagement;

            bool isLinking = timedState.IsLinking;

            if (config.AdminRoles != null && config.AdminRoles.Length > 0)
            {
                if (timedState.Admin)
                {
                    _logger.LogInformation(
                        "OpenID user {Username} matched one of the configured admin roles {@AdminRoles}.",
                        timedState.Username,
                        config.AdminRoles);
                }
                else
                {
                    _logger.LogWarning(
                        "OpenID user {Username} did not match any configured admin role {@AdminRoles}. RoleClaim={RoleClaim}. Claims seen: {@Claims}. Role matching is case-sensitive and exact-string.",
                        timedState.Username,
                        config.AdminRoles,
                        config.RoleClaim,
                        result.User.Claims.Select(o => new { o.Type, o.Value }));
                }
            }

            if (timedState.Valid)
            {
                _logger.LogInformation($"Is request linking: {isLinking}");
                return Content(WebResponse.Generator(data: state, provider: provider, baseUrl: GetRequestBase(config.SchemeOverride, config.PortOverride), mode: "OID", isLinking: isLinking), MediaTypeNames.Text.Html);
            }
            else
            {
                _logger.LogWarning(
                    "OpenID user {Username} has one or more incorrect role claims: {@Claims}. Expected any one of: {@ExpectedClaims}",
                    timedState.Username,
                    result.User.Claims.Select(o => new { o.Type, o.Value }),
                    config.Roles);

                return ReturnError(StatusCodes.Status401Unauthorized, "Error. Check permissions.");
            }
        }

        // If the config doesn't have an active provider matching the requeset, show an error
        return BadRequest("No matching provider found");
    }

    /// <summary>
    /// Initiates the login flow for OpenID. This redirects the user to the auth provider.
    /// </summary>
    /// <param name="provider">The name of the provider.</param>
    /// <param name="isLinking">Whether or not this request is to link accounts (Rather than authenticate).</param>
    /// <returns>An asynchronous result for the authentication.</returns>
    [HttpGet("OID/p/{provider}")]
    [HttpGet("OID/start/{provider}")]
    public async Task<ActionResult> OidChallenge(string provider, [FromQuery] bool isLinking = false)
    {
        Invalidate();
        OidConfig config;
        try
        {
            config = SSOPlugin.Instance.Configuration.OidConfigs[provider];
        }
        catch (KeyNotFoundException)
        {
            throw new ArgumentException("Provider does not exist");
        }

        if (config.Enabled)
        {
            bool newPath = config.NewPath;
            if (!isLinking)
            {
                newPath = Request.Path.Value.Contains("/start/", StringComparison.InvariantCultureIgnoreCase);
                config.NewPath = newPath;
            }

            string redirectUri = GetRequestBase(config.SchemeOverride, config.PortOverride) + $"/sso/OID/{(newPath ? "redirect" : "r")}/" + provider;

            var options = new OidcClientOptions
            {
                Authority = config.OidEndpoint?.Trim(),
                ClientId = config.OidClientId?.Trim(),
                ClientSecret = config.OidSecret?.Trim(),
                RedirectUri = redirectUri,
                Scope = string.Join(" ", config.OidScopes.Prepend("openid profile")),
                DisablePushedAuthorization = config.DisablePushedAuthorization,
                LoggerFactory = _loggerFactory,
                LoadProfile = !config.DoNotLoadProfile,
                HttpClientFactory = o =>
                {
                    var client = _httpClientFactory.CreateClient();
                    System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
                    string version = fvi.FileVersion;

                    client.DefaultRequestHeaders.UserAgent.ParseAdd($"Jellyfin-Plugin-SSO-Auth +{version} (https://github.com/k0lin/jellyfin-plugin-sso)");
                    return client;
                }
            };
            var oidEndpointUri = new Uri(config.OidEndpoint?.Trim());
            options.Policy.Discovery.AdditionalEndpointBaseAddresses.Add(oidEndpointUri.GetLeftPart(UriPartial.Authority));
            options.Policy.Discovery.ValidateEndpoints = !config.DoNotValidateEndpoints; // For Google and other providers with different endpoints
            options.Policy.Discovery.RequireHttps = !config.DisableHttps;
            options.Policy.Discovery.ValidateIssuerName = !config.DoNotValidateIssuerName;
            var oidcClient = new OidcClient(options);
            var state = await oidcClient.PrepareLoginAsync().ConfigureAwait(false);

            if (state.IsError)
            {
                return ReturnError(StatusCodes.Status400BadRequest, $"Error preparing login: {state.Error} - {state.ErrorDescription}");
            }

            StateManager[state.State] = new TimedAuthorizeState(state, DateTime.Now)
            {
                IsLinking = isLinking
            };
            return Redirect(state.StartUrl);
        }

        throw new ArgumentException("Provider does not exist");
    }

    /// <summary>
    /// Adds an OpenID auth configuration. Requires administrator privileges. If the provider already exists, it will be removed and readded.
    /// </summary>
    /// <param name="provider">The name of the provider to add.</param>
    /// <param name="config">The OID configuration (deserialized from a JSON post).</param>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpPost("OID/Add/{provider}")]
    public void OidAdd(string provider, [FromBody] OidConfig config)
    {
        var configuration = SSOPlugin.Instance.Configuration;
        configuration.OidConfigs[provider] = config;
        SSOPlugin.Instance.UpdateConfiguration(configuration);
    }

    /// <summary>
    /// Deletes an OpenID provider.
    /// </summary>
    /// <param name="provider">Name of provider to delete.</param>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpGet("OID/Del/{provider}")]
    public void OidDel(string provider)
    {
        var configuration = SSOPlugin.Instance.Configuration;
        configuration.OidConfigs.Remove(provider);
        SSOPlugin.Instance.UpdateConfiguration(configuration);
    }

    /// <summary>
    /// Lists the OpenID providers configured. Requires administrator privileges.
    /// </summary>
    /// <returns>The list of OpenID configurations.</returns>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpGet("OID/Get")]
    public ActionResult OidProviders()
    {
        return Ok(SSOPlugin.Instance.Configuration.OidConfigs);
    }

    /// <summary>
    /// Lists the OpenID providers names only.
    /// </summary>
    /// <returns>The list of OpenID configurations.</returns>
    [HttpGet("OID/GetNames")]
    public ActionResult OidProviderNames()
    {
        return Ok(SSOPlugin.Instance.Configuration.OidConfigs.Keys);
    }

    /// <summary>
    /// Lists the SAML providers names only.
    /// </summary>
    /// <returns>The list of OpenID configurations.</returns>
    [HttpGet("SAML/GetNames")]
    public ActionResult SamlProviderNames()
    {
        return Ok(SSOPlugin.Instance.Configuration.SamlConfigs.Keys);
    }

    /// <summary>
    /// This is a debug endpoint to list all running OpenID flows. Requires administrator privileges.
    /// </summary>
    /// <returns>The list of OpenID flows in progress.</returns>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpGet("OID/States")]
    public ActionResult OidStates()
    {
        return Ok(StateManager);
    }

    /// <summary>
    /// This endpoint accepts JSON and will authorize the user from the device values passed from the client.
    /// </summary>
    /// <param name="provider">Name of provider to authenticate against.</param>
    /// <param name="response">The data passed to the client to ensure it is the right one.</param>
    /// <returns>JSON for the client to populate information with.</returns>
    [HttpPost("OID/Auth/{provider}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> OidAuth(string provider, [FromBody] AuthResponse response)
    {
        OidConfig config;
        try
        {
            config = SSOPlugin.Instance.Configuration.OidConfigs[provider];
        }
        catch (KeyNotFoundException)
        {
            return BadRequest("No matching provider found");
        }

        if (config.Enabled)
        {
            foreach (var kvp in StateManager)
            {
                if (kvp.Value.State.State.Equals(response.Data) && kvp.Value.Valid && StateManager.TryRemove(kvp.Key, out var timedState))
                {
                    Guid userId = await CreateCanonicalLinkAndUserIfNotExist("oid", provider, timedState.Username, config.EnableAuthorization);

                    var authenticationResult = await Authenticate(
                        userId,
                        timedState.Admin,
                        config.EnableAuthorization,
                        config.EnableAllFolders,
                        timedState.Folders.ToArray(),
                        timedState.EnableLiveTv,
                        timedState.EnableLiveTvManagement,
                        response,
                        config.DefaultProvider?.Trim(),
                        timedState.AvatarURL,
                        config.PreserveAdminPermissions)
                        .ConfigureAwait(false);
                    return Ok(authenticationResult);
                }
            }
        }

        return Problem("Something went wrong");
    }

    /// <summary>
    /// This is the callback for the SAML flow. This creates a webpage to complete auth.
    /// </summary>
    /// <param name="provider">The provider that is calling back.</param>
    /// <param name="relayState">
    ///    RelayState given in the original saml request. If it is equal to "linking",
    ///    We consider this to be a linking request.
    /// </param>
    /// <returns>A webpage that will complete the client-side flow.</returns>
    [HttpPost("SAML/p/{provider}")]
    [HttpPost("SAML/post/{provider}")]
    public ActionResult SamlPost(string provider, [FromQuery] string relayState = null)
    {
        SamlConfig config;
        try
        {
            config = SSOPlugin.Instance.Configuration.SamlConfigs[provider];
        }
        catch (KeyNotFoundException)
        {
            return BadRequest("No matching provider found");
        }

        bool isLinking = relayState == "linking";

        _logger.LogInformation(
            $"SAML request has relayState of {relayState}");

        if (config.Enabled)
        {
            var samlResponse = new Response(config.SamlCertificate, Request.Form["SAMLResponse"]);

            if (!samlResponse.IsValid())
            {
                return Problem("Invalid SAML signature");
            }

            var samlRoles = samlResponse.GetCustomAttributes("Role").ToList();
            var samlAuthorization = AuthorizationEvaluator.Evaluate(
                samlRoles,
                config.Roles,
                config.AdminRoles,
                Array.Empty<string>(),
                config.EnableFolderRoles,
                config.FolderRoleMapping,
                config.EnableLiveTv,
                config.EnableLiveTvManagement,
                config.EnableLiveTvRoles,
                config.LiveTvRoles,
                config.LiveTvManagementRoles);

            if (samlAuthorization.IsValid)
            {
                return Content(
                        WebResponse.Generator(
                            data: Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(samlResponse.Xml)),
                            provider: provider,
                            baseUrl: GetRequestBase(config.SchemeOverride, config.PortOverride),
                            mode: "SAML",
                            isLinking: isLinking),
                        MediaTypeNames.Text.Html);
            }

            _logger.LogWarning(
                "SAML user: {UserId} has insufficient roles: {@Roles}. Expected any one of: {@ExpectedRoles}",
                samlResponse.GetNameID(),
                samlResponse.GetCustomAttributes("Role"),
                config.Roles);
            return ReturnError(StatusCodes.Status401Unauthorized, "Error. Check permissions.");
        }

        return ReturnError(StatusCodes.Status400BadRequest, "No active providers found");
    }

    /// <summary>
    /// Initializes the SAML flow. This will redirect the user to the SAML provider.
    /// </summary>
    /// <param name="provider">The provider to being the flow with.</param>
    /// <param name="isLinking">Whether this flow intends to link an account, or initiate auth.</param>
    /// <returns>A redirect to the SAML provider's auth page.</returns>
    [HttpGet("SAML/p/{provider}")]
    [HttpGet("SAML/start/{provider}")]
    public RedirectResult SamlChallenge(string provider, [FromQuery] bool isLinking = false)
    {
        SamlConfig config;
        try
        {
            config = SSOPlugin.Instance.Configuration.SamlConfigs[provider];
        }
        catch (KeyNotFoundException)
        {
            throw new ArgumentException("Provider does not exist");
        }

        if (config.Enabled)
        {
            bool newPath = config.NewPath;
            if (!isLinking)
            {
                newPath = Request.Path.Value.Contains("/start/", StringComparison.InvariantCultureIgnoreCase);
                config.NewPath = newPath;
            }

            string redirectUri = GetRequestBase(config.SchemeOverride, config.PortOverride) + $"/sso/SAML/{(newPath ? "post" : "p")}/" + provider;
            string relayState = null;
            if (isLinking)
            {
                relayState = "linking";
            }

            var request = new AuthRequest(
                config.SamlClientId.Trim(),
                redirectUri);

            return Redirect(request.GetRedirectUrl(config.SamlEndpoint.Trim(), relayState));
        }

        throw new ArgumentException("Provider does not exist");
    }

    /// <summary>
    /// Adds a SAML configuration. If the provider already exists, overwrite it.
    /// </summary>
    /// <param name="provider">The provider name to add.</param>
    /// <param name="newConfig">The SAML configuration object (deserialized) from JSON.</param>
    /// <returns>The success result.</returns>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpPost("SAML/Add/{provider}")]
    public OkResult SamlAdd(string provider, [FromBody] SamlConfig newConfig)
    {
        var configuration = SSOPlugin.Instance.Configuration;
        configuration.SamlConfigs[provider] = newConfig;
        SSOPlugin.Instance.UpdateConfiguration(configuration);
        return Ok();
    }

    /// <summary>
    /// Deletes a provider from the configuration with a given ID.
    /// </summary>
    /// <param name="provider">The ID of the provider to delete.</param>
    /// <returns>The success result.</returns>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpGet("SAML/Del/{provider}")]
    public OkResult SamlDel(string provider)
    {
        var configuration = SSOPlugin.Instance.Configuration;
        configuration.SamlConfigs.Remove(provider);
        SSOPlugin.Instance.UpdateConfiguration(configuration);
        return Ok();
    }

    /// <summary>
    /// Returns a list of all SAML providers configured. Requires administrator privileges.
    /// </summary>
    /// <returns>A list of all of the Saml providers available.</returns>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpGet("SAML/Get")]
    public ActionResult SamlProviders()
    {
        return Ok(SSOPlugin.Instance.Configuration.SamlConfigs);
    }

    /// <summary>
    /// This endpoint accepts JSON and will authorize the user from the device values passed from the client.
    /// </summary>
    /// <param name="provider">The provider to authenticate against.</param>
    /// <param name="response">The data passed to the client to ensure it is the right one.</param>
    /// <returns>JSON for the client to populate information with.</returns>
    [HttpPost("SAML/Auth/{provider}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> SamlAuth(string provider, [FromBody] AuthResponse response)
    {
        SamlConfig config;
        try
        {
            config = SSOPlugin.Instance.Configuration.SamlConfigs[provider];
        }
        catch (KeyNotFoundException)
        {
            return BadRequest("No matching provider found");
        }

        if (config.Enabled)
        {
            var samlResponse = new Response(config.SamlCertificate, response.Data);

            if (!samlResponse.IsValid())
            {
                return Problem("Invalid SAML signature");
            }

            var defaultSamlFolders = !config.EnableFolderRoles && config.EnabledFolders != null ? config.EnabledFolders : Array.Empty<string>();

            var samlRoles = samlResponse.GetCustomAttributes("Role").ToList();
            var samlAuthorization = AuthorizationEvaluator.Evaluate(
                samlRoles,
                config.Roles,
                config.AdminRoles,
                defaultSamlFolders,
                config.EnableFolderRoles,
                config.FolderRoleMapping,
                config.EnableLiveTv,
                config.EnableLiveTvManagement,
                config.EnableLiveTvRoles,
                config.LiveTvRoles,
                config.LiveTvManagementRoles);

            if (config.AdminRoles != null && config.AdminRoles.Length > 0)
            {
                if (samlAuthorization.IsAdmin)
                {
                    _logger.LogInformation(
                        "SAML user {Username} matched one of the configured admin roles {@AdminRoles}.",
                        samlResponse.GetNameID(),
                        config.AdminRoles);
                }
                else
                {
                    _logger.LogWarning(
                        "SAML user {Username} did not match any configured admin role {@AdminRoles}. Role attributes seen: {@Roles}. Role matching is case-sensitive and exact-string.",
                        samlResponse.GetNameID(),
                        config.AdminRoles,
                        samlRoles);
                }
            }

            Guid userId = await CreateCanonicalLinkAndUserIfNotExist("saml", provider, samlResponse.GetNameID(), config.EnableAuthorization);

            var authenticationResult = await Authenticate(
                userId,
                samlAuthorization.IsAdmin,
                config.EnableAuthorization,
                config.EnableAllFolders,
                samlAuthorization.Folders.ToArray(),
                samlAuthorization.EnableLiveTv,
                samlAuthorization.EnableLiveTvManagement,
                response,
                config.DefaultProvider?.Trim(),
                null,
                config.PreserveAdminPermissions)
                .ConfigureAwait(false);
            return Ok(authenticationResult);
        }

        return Problem("Something went wrong");
    }

    /// <summary>
    /// Removes a user from SSO auth and switches it back to another auth provider. Requires administrator privileges.
    /// </summary>
    /// <param name="username">The username to switch to the new provider.</param>
    /// <param name="provider">The new provider to switch to.</param>
    /// <returns>Whether this API endpoint succeeded.</returns>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpPost("Unregister/{username}")]
    public async Task<ActionResult> Unregister(string username, [FromBody] string provider)
    {
        User user = _userManager.GetUserByName(username);
        if (user == null)
        {
            return NotFound("User not found");
        }

        user.AuthenticationProviderId = provider;
        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

        return Ok();
    }

    private SerializableDictionary<string, Guid> GetCanonicalLinks(string mode, string provider)
    {
        SerializableDictionary<string, Guid> links = null;

        switch (mode.ToLower())
        {
            case "saml":
                links = SSOPlugin.Instance.Configuration.SamlConfigs[provider].CanonicalLinks;
                break;
            case "oid":
                links = SSOPlugin.Instance.Configuration.OidConfigs[provider].CanonicalLinks;
                break;
            default:
                throw new ArgumentException($"{mode} is not a valid choice between 'saml' and 'oid'");
        }

        if (links == null)
        {
            links = new SerializableDictionary<string, Guid>();
        }

        return links;
    }

    private async Task<Guid> CreateCanonicalLinkAndUserIfNotExist(string mode, string provider, string canonicalName, bool enableAuthorization)
    {
        User user = null;

        // First try to get the user by its id in case it was already registered before
        Guid userId = Guid.Empty;
        try
        {
            userId = GetCanonicalLink(mode, provider, canonicalName);
        }
        catch (KeyNotFoundException)
        {
            userId = Guid.Empty;
        }

        // No userId found? Let's try and find the user by name instead
        if (userId == Guid.Empty)
        {
            user = _userManager.GetUserByName(canonicalName);
        }
        else
        {
            user = _userManager.GetUserById(userId);
        }

        if (user == null)
        {
            _logger.LogInformation($"SSO user {canonicalName} doesn't exist, creating...");
            user = await _userManager.CreateUserAsync(canonicalName).ConfigureAwait(false);

            if (!enableAuthorization)
            {
                var policy = _userManager.GetUserDto(user).Policy;
                policy.EnableAllFolders = false;
                policy.EnabledFolders = Array.Empty<Guid>();
                await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
                user = _userManager.GetUserById(user.Id);
            }

            user.AuthenticationProviderId = GetType().FullName;
            // https://jonathancrozier.com/blog/how-to-generate-a-cryptographically-secure-random-string-in-dot-net-with-c-sharp
            user.Password = _cryptoProvider.CreatePasswordHash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))).ToString();
            await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

            // Make sure there aren't any trailing existing links
            var links = GetCanonicalLinks(mode, provider);
            links.Remove(canonicalName);
            UpdateCanonicalLinkConfig(links, mode, provider);
        }

        userId = Guid.Empty;
        try
        {
            userId = GetCanonicalLink(mode, provider, canonicalName);
        }
        catch (KeyNotFoundException)
        {
            userId = Guid.Empty;
        }

        if (userId == Guid.Empty)
        {
            _logger.LogInformation("SSO user link doesn't exist, creating...");
            userId = user.Id;
            CreateCanonicalLink(mode, provider, userId, canonicalName);
        }

        return userId;
    }

    private Guid GetCanonicalLink(string mode, string provider, string canonicalName)
    {
        SerializableDictionary<string, Guid> links = null;
        Guid userId = Guid.Empty;

        links = GetCanonicalLinks(mode, provider);

        userId = links[canonicalName];

        return userId;
    }

    /// <summary>
    /// Create a canonical link for a given user. Must be performed by the user being changed, or admin.
    /// </summary>
    /// <param name="mode">The mode of the function; SAML or OID.</param>
    /// <param name="provider">The name of the provider to link to a jellyfin account.</param>
    /// <param name="jellyfinUserId">The user ID within jellyfin to link to the provider.</param>
    /// <param name="authResponse">The client information to authenticate the user with.</param>
    /// <returns>Whether this API endpoint succeeded.</returns>
    [Authorize]
    [HttpPost("{mode}/Link/{provider}/{jellyfinUserId}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> AddCanonicalLink([FromRoute] string mode, [FromRoute] string provider, [FromRoute] Guid jellyfinUserId, [FromBody] AuthResponse authResponse)
    {
        if (!await RequestHelpers.AssertCanUpdateUser(_authContext, HttpContext.Request, jellyfinUserId, true).ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "User is not allowed to link SSO providers.");
        }

        switch (mode.ToLower())
        {
            case "saml":
                return SamlLink(provider, jellyfinUserId, authResponse);
            case "oid":
                return OidLink(provider, jellyfinUserId, authResponse);
            default:
                throw new ArgumentException($"{mode} is not a valid choice between 'saml' and 'oid'");
        }
    }

    /// <summary>
    /// Unregisters a given mapping from id within provider to user.
    /// </summary>
    /// <param name="mode">The mode of the function; SAML or OID.</param>
    /// <param name="provider">The name of the provider from which the link should be removed.</param>
    /// <param name="jellyfinUserId">The user ID within jellyfin to unlink from the provider.</param>
    /// <param name="canonicalName">The user ID within jellyfin to unlink.</param>
    /// <returns>Whether this API endpoint succeeded.</returns>
    [Authorize]
    [HttpDelete("{mode}/Link/{provider}/{jellyfinUserId}/{canonicalName}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> DeleteCanonicalLink([FromRoute] string mode, [FromRoute] string provider, [FromRoute] Guid jellyfinUserId, [FromRoute] string canonicalName)
    {
        if (!await RequestHelpers.AssertCanUpdateUser(_authContext, HttpContext.Request, jellyfinUserId, true).ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Current user is not allowed to unlink SSO providers for user ID.");
        }

        Guid linkedId = GetCanonicalLink(mode, provider, canonicalName);

        if (linkedId != jellyfinUserId)
        {
            return StatusCode(StatusCodes.Status409Conflict, "jellyfin UID does not match id registered to that canonical name.");
        }

        var links = GetCanonicalLinks(mode, provider);

        links.Remove(canonicalName);

        return UpdateCanonicalLinkConfig(links, mode, provider);
    }

    /// <summary>
    /// Gets all the saml links for a user.
    /// </summary>
    /// <param name="jellyfinUserId">The user ID within jellyfin for which to return the links.</param>
    /// <returns>A dictionary of provider : link mappings.</returns>
    [Authorize]
    [HttpGet("saml/links/{jellyfinUserId}")]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult<SerializableDictionary<string, IEnumerable<string>>>> GetSamlLinksByUser(Guid jellyfinUserId)
    {
        if (!await RequestHelpers.AssertCanUpdateUser(_authContext, HttpContext.Request, jellyfinUserId, true).ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Non-admin is not allowed to query other user's mappings.");
        }

        var mappings = new SerializableDictionary<string, IEnumerable<string>>();
        var providerList = SSOPlugin.Instance.Configuration.SamlConfigs;

        foreach (var providerName in providerList.Keys)
        {
            var canonLinks = providerList[providerName].CanonicalLinks;
            var canonKeys = from link in canonLinks where link.Value == jellyfinUserId select link.Key;
            mappings[providerName] = canonKeys;
        }

        return mappings;
    }

    /// <summary>
    /// Gets all the oid links for a user.
    /// </summary>
    /// <param name="jellyfinUserId">The user ID within jellyfin for which to return the links.</param>
    /// <returns>A dictionary of provider : link mappings.</returns>
    [Authorize]
    [HttpGet("oid/links/{jellyfinUserId}")]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult<SerializableDictionary<string, IEnumerable<string>>>> GetOidLinksByUser(Guid jellyfinUserId)
    {
        if (!await RequestHelpers.AssertCanUpdateUser(_authContext, HttpContext.Request, jellyfinUserId, true).ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Non-admin is not allowed to query other user's mappings.");
        }

        var mappings = new SerializableDictionary<string, IEnumerable<string>>();
        var providerList = SSOPlugin.Instance.Configuration.OidConfigs;

        foreach (var providerName in providerList.Keys)
        {
            var canonLinks = providerList[providerName].CanonicalLinks;
            var canonKeys = from link in canonLinks where link.Value == jellyfinUserId select link.Key;
            mappings[providerName] = canonKeys;
        }

        return mappings;
    }

    /// <summary>
    /// Validate a saml link request and create the link if it is valid.
    /// </summary>
    /// <param name="provider">The provider to authenticate against.</param>
    /// <param name="jellyfinUserId">
    ///   The ID of the account to be linked to the provider.
    ///   Must be performed by this user, or an admin.
    /// </param>
    /// <param name="response">The data passed to the client to ensure it is the right one.</param>
    /// <returns>JSON for the client to populate information with.</returns>
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    private ActionResult SamlLink(string provider, Guid jellyfinUserId, AuthResponse response)
    {
        SamlConfig config;
        try
        {
            config = SSOPlugin.Instance.Configuration.SamlConfigs[provider];
        }
        catch (KeyNotFoundException)
        {
            return BadRequest("No matching provider found");
        }

        var samlResponse = new Response(config.SamlCertificate, response.Data);

        if (!samlResponse.IsValid())
        {
            return Problem("Invalid SAML signature");
        }

        string providerUserId = samlResponse.GetNameID();

        return CreateCanonicalLink("saml", provider, jellyfinUserId, providerUserId);
    }

    /// <summary>
    /// Validate an OIDC link request and create the link if it is valid.
    /// </summary>
    /// <param name="provider">The provider to authenticate against.</param>
    /// <param name="jellyfinUserId">
    ///   The ID of the account to be linked to the provider.
    ///   Must be performed by this user, or an admin.
    /// </param>
    /// <param name="response">The data passed to the client to ensure it is the right one.</param>
    /// <returns>JSON for the client to populate information with.</returns>
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    private ActionResult OidLink(string provider, Guid jellyfinUserId, AuthResponse response)
    {
        OidConfig config;
        try
        {
            config = SSOPlugin.Instance.Configuration.OidConfigs[provider];
        }
        catch (KeyNotFoundException)
        {
            return BadRequest("No matching provider found");
        }

        foreach (var kvp in StateManager)
        {
            if (kvp.Value.State.State.Equals(response.Data) && kvp.Value.Valid)
            {
                string providerUserId = kvp.Value.Username;
                return CreateCanonicalLink("oid", provider, jellyfinUserId, providerUserId);
            }
        }

        return Problem("Something went wrong!");
    }

    private ActionResult CreateCanonicalLink(string mode, string provider, [FromRoute] Guid jellyfinUserId, string providerUserId)
    {
        SerializableDictionary<string, Guid> links = null;
        try
        {
            links = GetCanonicalLinks(mode, provider);
        }
        catch (KeyNotFoundException)
        {
            return BadRequest("No matching provider found");
        }

        links[providerUserId] = jellyfinUserId;
        UpdateCanonicalLinkConfig(links, mode, provider);

        return NoContent();
    }

    private OkResult UpdateCanonicalLinkConfig(SerializableDictionary<string, Guid> links, string mode, string provider)
    {
        var configuration = SSOPlugin.Instance.Configuration;
        switch (mode.ToLower())
        {
            case "saml":
                configuration.SamlConfigs[provider].CanonicalLinks = links;
                break;
            case "oid":
                configuration.OidConfigs[provider].CanonicalLinks = links;
                break;
            default:
                throw new ArgumentException($"{mode} is not a valid choice between 'saml' and 'oid'");
        }

        SSOPlugin.Instance.UpdateConfiguration(configuration);
        return Ok();
    }

    /// <summary>
    /// Authenticates the user with the given information.
    /// </summary>
    /// <param name="userId">The user id of the user to authenticate.</param>
    /// <param name="isAdmin">Determines whether this user is an administrator.</param>
    /// <param name="enableAuthorization">Determines whether RBAC is used for this user.</param>
    /// <param name="enableAllFolders">Determines whether all folders are enabled.</param>
    /// <param name="enabledFolders">Determines which folders should be enabled for this client.</param>
    /// <param name="enableLiveTv">Determines whether live TV access is allowed for this user.</param>
    /// <param name="enableLiveTvAdmin">Determines whether live TV can be managed by this user.</param>
    /// <param name="authResponse">The client information to authenticate the user with.</param>
    /// <param name="defaultProvider">The default provider of the user to be set after logging in.</param>
    /// <param name="avatarUrl">The new avatar url for the user.</param>
    /// <param name="preserveAdmin">When true, existing administrator permissions are never revoked because the current login did not match an admin role.</param>
    private async Task<AuthenticationResult> Authenticate(Guid userId, bool isAdmin, bool enableAuthorization, bool enableAllFolders, string[] enabledFolders, bool enableLiveTv, bool enableLiveTvAdmin, AuthResponse authResponse, string defaultProvider, string avatarUrl, bool preserveAdmin)
    {
        User user = _userManager.GetUserById(userId);

        // Jellyfin 10.11 persists permission/preference rows reliably through the policy path.
        var policy = _userManager.GetUserDto(user).Policy;

        // UpdatePolicyAsync clears and re-adds schedules; give it fresh, untracked instances.
        policy.AccessSchedules = (policy.AccessSchedules ?? Array.Empty<AccessSchedule>())
            .Select(schedule => new AccessSchedule(schedule.DayOfWeek, schedule.StartHour, schedule.EndHour, userId))
            .ToArray();

        // GetUserDto does not populate this permission on some Jellyfin versions; preserve it.
        policy.EnableLyricManagement = user.HasPermission(PermissionKind.EnableLyricManagement);

        if (enableAuthorization)
        {
            if (isAdmin)
            {
                _logger.LogInformation("User {Username} matched an admin role; granting IsAdministrator.", user.Username);
                policy.IsAdministrator = true;
            }
            else if (preserveAdmin && policy.IsAdministrator)
            {
                _logger.LogInformation(
                    "User {Username} did not match an admin role in this login, but PreserveAdminPermissions is enabled; leaving IsAdministrator unchanged.",
                    user.Username);
            }
            else
            {
                if (policy.IsAdministrator)
                {
                    _logger.LogWarning(
                        "User {Username} did not match an admin role; revoking IsAdministrator because PreserveAdminPermissions is disabled.",
                        user.Username);
                }

                policy.IsAdministrator = false;
            }

            policy.EnableAllFolders = enableAllFolders;
            if (!enableAllFolders)
            {
                var folderIds = new List<Guid>();
                foreach (string folder in enabledFolders ?? Array.Empty<string>())
                {
                    if (Guid.TryParse(folder, out var folderId))
                    {
                        folderIds.Add(folderId);
                    }
                    else
                    {
                        _logger.LogWarning("Ignoring enabled folder {Folder}: not a valid folder id.", folder);
                    }
                }

                policy.EnabledFolders = folderIds.ToArray();
            }
        }

        policy.EnableLiveTvAccess = enableLiveTv;
        policy.EnableLiveTvManagement = enableLiveTvAdmin;

        if (!string.IsNullOrEmpty(defaultProvider))
        {
            policy.AuthenticationProviderId = defaultProvider;
            _logger.LogInformation("Set default login provider to " + defaultProvider);
        }

        await _userManager.UpdatePolicyAsync(userId, policy).ConfigureAwait(false);

        // UpdatePolicyAsync refreshes the cached user entity; use the current instance below.
        user = _userManager.GetUserById(userId);

        if (avatarUrl is not null)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();

                System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
                System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
                string version = fvi.FileVersion;
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"Jellyfin-Plugin-SSO-Auth +{version} (https://github.com/k0lin/jellyfin-plugin-sso)");

                var avatarResponse = await client.GetAsync(avatarUrl);

                if (!avatarResponse.Content.Headers.TryGetValues("content-type", out var contentTypeList))
                {
                    throw new Exception("Cannot get Content-Type of image : " + avatarUrl);
                }

                var contentType = contentTypeList.First();
                if (!contentType.StartsWith("image"))
                {
                    throw new Exception("Content type of avatar URL is not an image, got :  " + contentType);
                }

                var extension = contentType.Split("/").Last();
                var stream = await avatarResponse.Content.ReadAsStreamAsync();

                if (user != null)
                {
                    var userDataPath =
                        Path.Combine(
                            _serverConfigurationManager.ApplicationPaths.UserConfigurationDirectoryPath,
                            user.Username);
                    if (user.ProfileImage is not null)
                    {
                        await _userManager.ClearProfileImageAsync(user).ConfigureAwait(false);
                    }

                    user.ProfileImage = new ImageInfo(Path.Combine(userDataPath, "profile" + extension));

                    await _providerManager.SaveImage(stream, contentType, user.ProfileImage.Path)
                        .ConfigureAwait(false);
                    await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
            }
        }

        var authRequest = new AuthenticationRequest();
        authRequest.UserId = user.Id;
        authRequest.Username = user.Username;
        authRequest.App = authResponse.AppName;
        authRequest.AppVersion = authResponse.AppVersion;
        authRequest.DeviceId = authResponse.DeviceID;
        authRequest.DeviceName = authResponse.DeviceName;
        _logger.LogInformation("Auth request created...");

        return await _sessionManager.AuthenticateDirect(authRequest).ConfigureAwait(false);
    }

    private void Invalidate()
    {
        var now = DateTime.Now;
        foreach (var kvp in StateManager)
        {
            if (now.Subtract(kvp.Value.Created).TotalMinutes > 1)
            {
                StateManager.TryRemove(kvp.Key, out _);
            }
        }
    }

    private string GetRequestBase(string schemeOverride = null, int? portOverride = null)
    {
        int requestPort;

        if (portOverride != null)
        {
            requestPort = portOverride.Value;
        }
        else
        {
            requestPort = Request.Host.Port ?? -1;
        }

        if ((requestPort == 80 && string.Equals(Request.Scheme, "http", StringComparison.OrdinalIgnoreCase)) || (requestPort == 443 && string.Equals(Request.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
        {
            requestPort = -1;
        }

        if (schemeOverride != "http" && schemeOverride != "https")
        {
            schemeOverride = null;
        }

        return new UriBuilder
        {
            Scheme = schemeOverride ?? Request.Scheme,
            Host = Request.Host.Host,
            Port = requestPort,
            Path = Request.PathBase
        }.ToString().TrimEnd('/');
    }

    private ContentResult ReturnError(int code, string message)
    {
        var errorResult = new ContentResult();
        errorResult.Content = message;
        errorResult.ContentType = MediaTypeNames.Text.Plain;
        errorResult.StatusCode = code;
        return errorResult;
    }
}

/// <summary>
/// The data the client should pass back to the API.
/// </summary>
public class AuthResponse
{
    /// <summary>
    /// Gets or sets the device ID of the client.
    /// </summary>
    public string DeviceID { get; set; }

    /// <summary>
    /// Gets or sets the device name of the client.
    /// </summary>
    public string DeviceName { get; set; }

    /// <summary>
    /// Gets or sets the app name of the client.
    /// </summary>
    public string AppName { get; set; }

    /// <summary>
    /// Gets or sets the app version of the client.
    /// </summary>
    public string AppVersion { get; set; }

    /// <summary>
    /// Gets or sets the auth data of the client (for authorizing the response).
    /// </summary>
    public string Data { get; set; }
}

/// <summary>
/// A manager for OpenID to manage the state of the clients.
/// </summary>
public class TimedAuthorizeState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TimedAuthorizeState"/> class.
    /// </summary>
    /// <param name="state">The AuthorizeState to time.</param>
    /// <param name="created">When this state was created.</param>
    public TimedAuthorizeState(AuthorizeState state, DateTime created)
    {
        State = state;
        Created = created;
        Valid = false;
        Admin = false;
        IsLinking = false;
        EnableLiveTv = false;
        EnableLiveTvManagement = false;
        AvatarURL = null;
    }

    /// <summary>
    /// Gets or sets the Authorization State of the client.
    /// </summary>
    public AuthorizeState State { get; set; }

    /// <summary>
    /// Gets or sets when this object was created to time it out.
    /// </summary>
    public DateTime Created { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is valid.
    /// </summary>
    public bool Valid { get; set; }

    /// <summary>
    /// Gets or sets the user tied to the state.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is an administrator.
    /// </summary>
    public bool Admin { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the state is
    /// tied to a linking flow (instead of a login flow).
    /// </summary>
    public bool IsLinking { get; set; }

    /// <summary>
    /// Gets or sets the folders the user is allowed access to.
    /// </summary>
    public List<string> Folders { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is allowed to view live TV.
    /// </summary>
    public bool EnableLiveTv { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is allowed to manage live TV.
    /// </summary>
    public bool EnableLiveTvManagement { get; set; }

    /// <summary>
    /// Gets or sets the user avatar url.
    /// </summary>
    public string AvatarURL { get; set; }
}
