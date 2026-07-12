using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Jellyfin.Plugin.SSO_Auth.Tests.I18n;

public class TranslationCompletenessTests
{
    private static readonly string I18nPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "i18n"));

    private static HashSet<string> GetKeys(string filePath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
        return new HashSet<string>(FlattenKeys(doc.RootElement, string.Empty));
    }

    private static IEnumerable<string> FlattenKeys(JsonElement element, string prefix)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";
                foreach (var child in FlattenKeys(property.Value, key))
                    yield return child;
            }
        }
        else
        {
            yield return prefix;
        }
    }

    public static IEnumerable<object[]> NonReferenceLocales() =>
        Directory.GetFiles(I18nPath, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name != "en-us")
            .Select(name => new object[] { name! });

    [Theory]
    [MemberData(nameof(NonReferenceLocales))]
    public void AllReferenceKeysPresent(string locale)
    {
        var referenceKeys = GetKeys(Path.Combine(I18nPath, "en-us.json"));
        var localeKeys = GetKeys(Path.Combine(I18nPath, $"{locale}.json"));

        var missing = referenceKeys.Except(localeKeys).OrderBy(k => k).ToList();
        Assert.True(
            missing.Count == 0,
            $"Locale '{locale}' is missing {missing.Count} key(s):\n  {string.Join("\n  ", missing)}");
    }

    [Theory]
    [MemberData(nameof(NonReferenceLocales))]
    public void NoExtraKeys(string locale)
    {
        var referenceKeys = GetKeys(Path.Combine(I18nPath, "en-us.json"));
        var localeKeys = GetKeys(Path.Combine(I18nPath, $"{locale}.json"));

        var extra = localeKeys.Except(referenceKeys).OrderBy(k => k).ToList();
        Assert.True(
            extra.Count == 0,
            $"Locale '{locale}' has {extra.Count} extra key(s) not present in en-us:\n  {string.Join("\n  ", extra)}");
    }
}
