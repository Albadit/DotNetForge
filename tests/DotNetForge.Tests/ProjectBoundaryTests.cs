using System.Xml.Linq;
using DotNetForge.Infrastructure.Configuration;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// The project boundaries (.docs/architecture/dependencies.md#project-references): what each project may reference -
/// projects, NuGet packages, the ASP.NET Core framework - is listed below, and nothing else is allowed. A project
/// without an entry fails too, so a new one is placed deliberately. Within a project the compiler keeps the code to
/// what the project references. Read from the .csproj files; the analyzer reference every project gets from
/// Directory.Build.props (build/DotNetForge.Analyzers) is compiler-only and not listed.
/// </summary>
public sealed class ProjectBoundaryTests
{
    private sealed record Allowed(string[] Projects, string[] Packages, bool AspNetCore = false, string? Why = null);

    private static readonly Dictionary<string, Allowed> Rules = new(StringComparer.Ordinal)
    {
        // Bottom: contracts, shared types, pure libraries. They know nothing of the CMS layers above them.
        ["DotNetForge.Abstractions"] = new([], [], Why: "dependency-free and entity-free"),
        ["DotNetForge.Shared"] = new([], [], Why: "shared by every layer, so it depends on none"),
        ["DotNetForge.PageBuilder"] = new([], ["Newtonsoft.Json"], Why: "pure document logic"),
        ["DotNetForge.UI"] = new([], [], AspNetCore: true, Why: "the component library of the CMS and the extensions knows no CMS data"),

        // Domain and data, side by side: Core never sees EF Core, Data never sees Core (a store interface in Shared bridges them).
        ["DotNetForge.Core"] = new(["DotNetForge.Abstractions", "DotNetForge.Shared", "DotNetForge.PageBuilder"], [],
            Why: "domain rules stay testable without EF Core or ASP.NET Core"),
        ["DotNetForge.Data"] = new(["DotNetForge.Abstractions", "DotNetForge.Shared"],
            [
                "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Relational", "Microsoft.EntityFrameworkCore.Sqlite",
                "Npgsql.EntityFrameworkCore.PostgreSQL", "Microsoft.EntityFrameworkCore.SqlServer", "MySql.EntityFrameworkCore",
                "MongoDB.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Design", "Microsoft.AspNetCore.DataProtection.EntityFrameworkCore",
            ],
            Why: "never Core: the domain doesn't leak into the database code"),
        ["DotNetForge.Infrastructure"] = new(["DotNetForge.Abstractions", "DotNetForge.Shared"], ["AWSSDK.S3"]),
        ["DotNetForge.Content"] = new(["DotNetForge.Abstractions", "DotNetForge.Shared", "DotNetForge.Core", "DotNetForge.Data"], [],
            Why: "one rule set for the admin and the API, without ASP.NET Core"),

        // The extension contract: extensions compile against it, so it stays small.
        ["DotNetForge.Sdk"] = new(["DotNetForge.Abstractions", "DotNetForge.Shared", "DotNetForge.UI"], [], AspNetCore: true),
        ["DotNetForge.Extensions"] = new(["DotNetForge.Abstractions", "DotNetForge.Shared", "DotNetForge.Core", "DotNetForge.Sdk"], []),

        // Top: the API and the host. Nothing references the host.
        ["DotNetForge.Api"] = new(
            ["DotNetForge.Abstractions", "DotNetForge.Shared", "DotNetForge.Core", "DotNetForge.Data", "DotNetForge.Content"], [],
            AspNetCore: true, Why: "host services only through contracts in Shared, never the host itself"),
        ["DotNetForge.Web"] = new(
            [
                "DotNetForge.Abstractions", "DotNetForge.Shared", "DotNetForge.Core", "DotNetForge.Data", "DotNetForge.Infrastructure",
                "DotNetForge.Extensions", "DotNetForge.Sdk", "DotNetForge.UI", "DotNetForge.Api", "DotNetForge.Content", "DotNetForge.PageBuilder",
            ],
            [
                "Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation", "Microsoft.AspNetCore.OpenApi", "Scalar.AspNetCore", "SkiaSharp",
                "SkiaSharp.NativeAssets.Linux.NoDependencies", "Fido2", "QRCoder",
            ],
            AspNetCore: true),

        // Tests: the unit tests stay below the web layer; the integration and browser tests drive the host.
        ["DotNetForge.Tests"] = new(
            [
                "DotNetForge.Abstractions", "DotNetForge.Shared", "DotNetForge.Core", "DotNetForge.Data", "DotNetForge.Infrastructure",
                "DotNetForge.Extensions", "DotNetForge.PageBuilder", "DotNetForge.UI",
            ],
            ["Microsoft.NET.Test.Sdk", "xunit", "xunit.runner.visualstudio"],
            Why: "unit tests: never the host or the API"),
        ["DotNetForge.IntegrationTests"] = new(
            ["DotNetForge.Web", "TestKit", "MarkdownKit", "ContactForm", "SeoToolkit"],
            ["Microsoft.NET.Test.Sdk", "xunit", "xunit.runner.visualstudio", "Microsoft.AspNetCore.Mvc.Testing"],
            Why: "the extension projects are built, not referenced (ReferenceOutputAssembly=false)"),
        ["DotNetForge.UiTests"] = new(
            ["DotNetForge.Web"],
            ["Microsoft.NET.Test.Sdk", "xunit", "xunit.runner.visualstudio", "Microsoft.AspNetCore.Mvc.Testing", "Microsoft.Playwright", "Deque.AxeCore.Playwright"]),
        ["TestKit"] = new([], [], Why: "an extension fixture, loaded by the host like any extension"),

        // Build tooling: runs at build or publish time, never part of the app.
        ["DotNetForge.Minify"] = new([], ["NUglify"]),
        ["DotNetForge.Analyzers"] = new([], ["Microsoft.CodeAnalysis.CSharp"]),
    };

    private sealed record ProjectFile(string Name, string Path, string[] Projects, string[] Packages, bool AspNetCore);

    private static readonly string Root = AppPaths.FindRepositoryRoot(AppContext.BaseDirectory)!;

    // out/ is a local publish (README "Test the production build locally"): it holds copies of the extensions' projects.
    private static readonly string PublishOutput = System.IO.Path.Combine(Root, "out") + System.IO.Path.DirectorySeparatorChar;

    private static readonly List<ProjectFile> Projects = Directory
        .EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories)
        .Where(path => !path.StartsWith(PublishOutput, StringComparison.OrdinalIgnoreCase))
        .Where(path => !path.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            .Any(part => part is "bin" or "obj" or "node_modules" or "TestResults"))
        .Select(Read)
        .OrderBy(p => p.Path, StringComparer.Ordinal)
        .ToList();

    [Fact]
    public void Every_project_has_a_place()
    {
        var unknown = Projects.Where(p => !IsExtension(p) && !Rules.ContainsKey(p.Name)).Select(p => p.Path).ToList();
        Assert.True(unknown.Count == 0,
            "Projects without an entry in ProjectBoundaryTests.Rules (and .docs/architecture/dependencies.md): " + string.Join(", ", unknown));

        var stale = Rules.Keys.Where(name => Projects.All(p => p.Name != name)).ToList();
        Assert.True(stale.Count == 0, "Rules for projects that no longer exist: " + string.Join(", ", stale));
    }

    [Fact]
    public void Projects_reference_only_what_their_layer_allows()
    {
        var problems = new List<string>();
        foreach (var project in Projects)
        {
            if (IsExtension(project))
            {
                // Extensions get the Sdk, Abstractions, Shared and UI from extensions/Directory.Build.props; anything else
                // of the host is off limits - the host loads them, nothing of it is theirs. Their packages are their own.
                problems.AddRange(project.Projects
                    .Where(name => !Projects.Any(p => p.Name == name && IsExtension(p)))
                    .Select(name => $"{project.Path} references {name}: an extension references only other extensions (its host contract comes from extensions/Directory.Build.props)"));
                continue;
            }

            if (!Rules.TryGetValue(project.Name, out var allowed))
            {
                continue; // Every_project_has_a_place
            }

            var why = allowed.Why is null ? "" : $" ({allowed.Why})";
            problems.AddRange(project.Projects.Except(allowed.Projects)
                .Select(name => $"{project.Path} references the project {name}, which its layer doesn't allow{why}"));
            problems.AddRange(project.Packages.Except(allowed.Packages)
                .Select(name => $"{project.Path} references the package {name}, which isn't listed for it{why}"));
            if (project.AspNetCore && !allowed.AspNetCore)
            {
                problems.Add($"{project.Path} uses ASP.NET Core, which its layer doesn't allow{why}");
            }
        }

        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    /// <summary>The test itself: a reference the rules don't list is reported.</summary>
    [Fact]
    public void The_rules_notice_what_they_forbid()
    {
        var core = Projects.Single(p => p.Name == "DotNetForge.Core");
        var leaky = core with { Projects = [.. core.Projects, "DotNetForge.Data"], Packages = ["Microsoft.EntityFrameworkCore"], AspNetCore = true };
        var allowed = Rules[leaky.Name];

        Assert.Contains("DotNetForge.Data", leaky.Projects.Except(allowed.Projects));
        Assert.Contains("Microsoft.EntityFrameworkCore", leaky.Packages.Except(allowed.Packages));
        Assert.False(allowed.AspNetCore);
    }

    private static bool IsExtension(ProjectFile project) => project.Path.StartsWith("extensions/", StringComparison.Ordinal);

    private static ProjectFile Read(string path)
    {
        var project = XDocument.Load(path).Root!;
        string[] Items(string kind) => project.Descendants(kind)
            .Select(item => (string?)item.Attribute("Include"))
            .OfType<string>()
            .ToArray();

        var sdk = (string?)project.Attribute("Sdk") ?? "";
        return new ProjectFile(
            System.IO.Path.GetFileNameWithoutExtension(path),
            System.IO.Path.GetRelativePath(Root, path).Replace('\\', '/'),
            // Written with \ or /; only / is a separator on Linux.
            Items("ProjectReference").Select(include => System.IO.Path.GetFileNameWithoutExtension(include.Replace('\\', '/'))).ToArray(),
            Items("PackageReference"),
            sdk == "Microsoft.NET.Sdk.Web" || Items("FrameworkReference").Contains("Microsoft.AspNetCore.App"));
    }
}
