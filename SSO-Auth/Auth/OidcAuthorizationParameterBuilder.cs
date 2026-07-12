using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Duende.IdentityModel.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.SSO_Auth.Auth;

/// <summary>
/// Builds validated provider-specific OIDC authorization parameters.
/// </summary>
internal static class OidcAuthorizationParameterBuilder
{
    private const int MaxParameterCount = 16;
    private const int MaxParameterValueLength = 8192;
    private const int MaxTotalLength = 16384;
    private static readonly Regex ParameterNamePattern = new Regex("^[A-Za-z][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ReservedParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "client_id",
        "redirect_uri",
        "response_type",
        "scope",
        "state",
        "nonce",
        "code_challenge",
        "code_challenge_method",
        "resource",
        "request",
        "request_uri"
    };

    /// <summary>
    /// Builds front-channel parameters from a JSON object.
    /// </summary>
    /// <param name="configuredParameters">The configured JSON object.</param>
    /// <returns>The validated front-channel parameters.</returns>
    /// <exception cref="ArgumentException">The configured parameters are invalid or unsafe.</exception>
    internal static Parameters Build(string configuredParameters)
    {
        if (string.IsNullOrWhiteSpace(configuredParameters))
        {
            return new Parameters();
        }

        JObject root;
        try
        {
            root = JObject.Parse(
                configuredParameters,
                new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        }
        catch (JsonException)
        {
            throw new ArgumentException("Authorization parameters must be a valid JSON object.", nameof(configuredParameters));
        }

        var properties = root.Properties().ToList();
        if (properties.Count > MaxParameterCount)
        {
            throw new ArgumentException($"Authorization parameters cannot contain more than {MaxParameterCount} entries.", nameof(configuredParameters));
        }

        var parameters = new Parameters();
        int totalLength = 0;
        foreach (JProperty property in properties)
        {
            ValidateName(property.Name);

            string value = GetValue(property);
            if (value.Length > MaxParameterValueLength)
            {
                throw new ArgumentException($"Authorization parameter {property.Name} is too long.", nameof(configuredParameters));
            }

            totalLength += property.Name.Length + value.Length;
            if (totalLength > MaxTotalLength)
            {
                throw new ArgumentException("Authorization parameters are too long.", nameof(configuredParameters));
            }

            parameters.Add(property.Name, value);
        }

        return parameters;
    }

    private static void ValidateName(string name)
    {
        if (!ParameterNamePattern.IsMatch(name))
        {
            throw new ArgumentException($"Authorization parameter {name} has an invalid name.");
        }

        if (ReservedParameters.Contains(name))
        {
            throw new ArgumentException($"Authorization parameter {name} cannot be overridden.");
        }
    }

    private static string GetValue(JProperty property)
    {
        if (string.Equals(property.Name, "claims", StringComparison.OrdinalIgnoreCase))
        {
            if (property.Value.Type != JTokenType.Object)
            {
                throw new ArgumentException("Authorization parameter claims must be a JSON object.");
            }

            return property.Value.ToString(Formatting.None);
        }

        return property.Value.Type switch
        {
            JTokenType.String => property.Value.Value<string>(),
            JTokenType.Boolean => property.Value.Value<bool>() ? "true" : "false",
            JTokenType.Integer => property.Value.Value<long>().ToString(CultureInfo.InvariantCulture),
            JTokenType.Float => property.Value.Value<double>().ToString(CultureInfo.InvariantCulture),
            _ => throw new ArgumentException($"Authorization parameter {property.Name} must have a scalar value.")
        };
    }
}
