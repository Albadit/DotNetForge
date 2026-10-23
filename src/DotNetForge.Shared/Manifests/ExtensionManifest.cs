using System.Text.Json.Serialization;

namespace DotNetForge.Shared.Manifests;

/// <summary>
/// The parsed contents of a <c>dotnetforge.extension.json</c> manifest. Property names map to the
/// camelCase JSON keys via <see cref="JsonPropertyNameAttribute"/>. The eight required fields are
/// <c>id, name, description, version, type, author, entryPoint, permissions</c>
/// (owned by .docs/features/extensions.md / .docs/architecture/codebase.md). There is exactly one <c>version</c> field.
/// </summary>
public sealed class ExtensionManifest
{
    /// <summary>The canonical manifest file name.</summary>
    public const string FileName = "dotnetforge.extension.json";

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("entryPoint")]
    public string? EntryPoint { get; set; }

    [JsonPropertyName("permissions")]
    public string[]? Permissions { get; set; }

    // Optional fields.

    /// <summary>
    /// The compiled code of the extension: a <c>.dll</c> path relative to the manifest's folder (normally
    /// <c>bin/&lt;Name&gt;.dll</c>). When set, the host loads it and instantiates <see cref="EntryPoint"/> (a full type
    /// name implementing <c>DotNetForge.Sdk.IExtension</c>); when absent the extension is views/resources only.
    /// </summary>
    [JsonPropertyName("assembly")]
    public string? Assembly { get; set; }

    [JsonPropertyName("website")]
    public string? Website { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();

    [JsonPropertyName("routes")]
    public List<string> Routes { get; set; } = new();

    [JsonPropertyName("settings")]
    public Dictionary<string, object?> Settings { get; set; } = new();
}
