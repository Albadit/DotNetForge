using System.Globalization;
using System.Text.Json;
using DotNetForge.Shared.Manifests;

namespace DotNetForge.Sdk;

/// <summary>
/// What the host tells an extension about itself: its manifest identity, its <c>settings</c>, its folder and where its
/// resources are served. Passed to <see cref="IExtension"/> and to the extension's views as
/// <c>ViewData["Extension"]</c>.
/// </summary>
public sealed class ExtensionContext
{
    private readonly IReadOnlyDictionary<string, object?> _raw;
    private readonly Func<string, string?> _environment;

    public ExtensionContext(ExtensionManifest manifest, string directory, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Id = manifest.Id ?? string.Empty;
        Name = manifest.Name ?? Id;
        Version = manifest.Version ?? string.Empty;
        Type = (manifest.Type ?? string.Empty).ToLowerInvariant();
        Directory = directory;
        _raw = manifest.Settings;
        _environment = environment;
        Settings = manifest.Settings.ToDictionary(kv => kv.Key, kv => Normalize(kv.Value), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The manifest <c>id</c>.</summary>
    public string Id { get; }

    public string Name { get; }

    public string Version { get; }

    /// <summary>The manifest <c>type</c>, lower case (<c>widget</c>, <c>theme</c>, ...).</summary>
    public string Type { get; }

    /// <summary>
    /// The extension's folder (where the manifest is). Read-only at runtime: never write here - use
    /// <c>IFileStorage</c> with an <c>ext/&lt;id&gt;/</c> key prefix or the database.
    /// </summary>
    public string Directory { get; }

    /// <summary>
    /// The manifest <c>settings</c>: JSON numbers as <see cref="long"/>/<see cref="double"/>, booleans as
    /// <see cref="bool"/>, strings as <see cref="string"/>, arrays and objects as their JSON text. Keys are
    /// case-insensitive.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Settings { get; }

    /// <summary>URL prefix of the extension's <c>Views/Resources/</c> files on the public site (themes and modules).</summary>
    public string PublicResourceBase => $"/ext/{Id}/resources";

    /// <summary>URL prefix of the extension's <c>Views/Resources/</c> files inside the admin area.</summary>
    public string AdminResourceBase => $"/admin/ext/{Id}/resources";

    public string? GetString(string key, string? fallback = null) =>
        Settings.TryGetValue(key, out var value) && value is string s ? s : fallback;

    public bool GetBool(string key, bool fallback = false) =>
        Settings.TryGetValue(key, out var value) && value is bool b ? b : fallback;

    public long GetInt64(string key, long fallback = 0) =>
        Settings.TryGetValue(key, out var value) && value is long l ? l : fallback;

    public double GetDouble(string key, double fallback = 0) =>
        Settings.TryGetValue(key, out var value) ? value switch
        {
            double d => d,
            long l => l,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => fallback,
        } : fallback;

    /// <summary>A setting that is a JSON array of strings (other values are skipped); empty when missing.</summary>
    public IReadOnlyList<string> GetStringList(string key)
    {
        var raw = _raw.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
        return raw is JsonElement { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
            : Array.Empty<string>();
    }

    /// <summary>
    /// A deployment value - process environment first, then <c>.env</c> - for secrets such as API keys, which must
    /// never be put in the manifest. Returns <c>null</c> when unset or empty.
    /// </summary>
    public string? GetEnvironmentValue(string name) => _environment(name) is { Length: > 0 } value ? value : null;

    /// <summary>Flattens a deserialized manifest value (a <see cref="JsonElement"/>) to a plain CLR value.</summary>
    public static object? Normalize(object? value) => value switch
    {
        JsonElement e => e.ValueKind switch
        {
            // The casts keep the ternary from widening every number to double.
            JsonValueKind.Number => e.TryGetInt64(out var l) ? (object)l : e.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => e.ToString(),
        },
        _ => value,
    };
}
