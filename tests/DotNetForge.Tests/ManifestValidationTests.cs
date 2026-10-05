using DotNetForge.Extensions;
using DotNetForge.Shared.Manifests;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// Validates the extension manifest validator against the required-field rules (.docs/features/extensions.md,
/// .docs/guides/testing.md). Error <c>Field</c> values use PascalCase property names.
/// </summary>
public sealed class ManifestValidationTests
{
    private readonly ManifestValidator _validator = new();

    private static ExtensionManifest ValidManifest() => new()
    {
        Id = "dotnetforge.theme.default",
        Name = "Default Theme",
        Description = "The default frontend theme for DotNetForge CMS.",
        Version = "1.0.0",
        Type = "theme",
        Author = "DotNetForge",
        EntryPoint = "DefaultTheme",
        Permissions = new[] { "content.read", "media.read" },
    };

    [Fact]
    public void Valid_manifest_passes()
    {
        var result = _validator.Validate(ValidManifest());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("Name")]
    [InlineData("Description")]
    [InlineData("Version")]
    [InlineData("Type")]
    [InlineData("Author")]
    [InlineData("EntryPoint")]
    public void Missing_required_field_is_reported(string field)
    {
        var manifest = ValidManifest();
        typeof(ExtensionManifest).GetProperty(field)!.SetValue(manifest, null);

        var result = _validator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == field);
    }

    [Fact]
    public void Empty_permissions_is_rejected()
    {
        var manifest = ValidManifest();
        manifest.Permissions = System.Array.Empty<string>();

        var result = _validator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "Permissions");
    }

    [Fact]
    public void Unknown_type_is_rejected()
    {
        var manifest = ValidManifest();
        manifest.Type = "not-a-real-type";

        var result = _validator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "Type");
    }

    [Fact]
    public void Non_semver_version_is_rejected()
    {
        var manifest = ValidManifest();
        manifest.Version = "one-point-oh";

        var result = _validator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "Version");
    }

    [Theory]
    [InlineData("theme")]
    [InlineData("authentication")]
    [InlineData("connector")]
    [InlineData("library")]
    [InlineData("admin")]
    [InlineData("widget")]
    [InlineData("provider")]
    [InlineData("plugin")]
    [InlineData("module")]
    public void All_documented_types_are_accepted(string type)
    {
        var manifest = ValidManifest();
        manifest.Type = type;

        Assert.True(_validator.Validate(manifest).IsValid);
    }
}
