using System.Diagnostics;
using NUglify;

// Minifies a published wwwroot in place (.docs/guides/deployment.md): the screens' scripts and stylesheets, and the UI
// library's scripts. Development serves the sources as they are; only `dotnet publish` in Release runs this. Left alone:
// the page builder's ES modules (their source is the fixture the C# renderer must match) and vendored libraries.
// A file that can't be minified is published as it is, with a warning: a slightly bigger file beats a broken release.
if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: DotNetForge.Minify <published wwwroot>");
    return 1;
}

var root = Path.GetFullPath(args[0]);
var saved = 0L;

foreach (var script in Files(root, ("js/admin", "*.js"), ("_content/DotNetForge.UI", "dnf*.js")))
{
    var source = File.ReadAllText(script);
    var result = Uglify.Js(source);
    if (result.HasErrors)
    {
        Warn(script, result.Errors[0].ToString());
        continue;
    }

    saved += Keep(script, source, result.Code);
}

var tailwind = FindTailwind();
if (tailwind is null)
{
    Console.WriteLine("warning: no Tailwind binary found, stylesheets are published as they are.");
}
else
{
    foreach (var stylesheet in Files(root, ("css", "*.css"), ("css/admin", "*.css")))
    {
        var output = Path.GetTempFileName();
        var process = Process.Start(new ProcessStartInfo(tailwind, ["--input", stylesheet, "--output", output, "--minify"])
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        })!;
        var log = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode == 0)
        {
            saved += Keep(stylesheet, File.ReadAllText(stylesheet), File.ReadAllText(output));
        }
        else
        {
            Warn(stylesheet, await log);
        }

        File.Delete(output);
    }
}

Console.WriteLine($"Minified the published assets: {saved / 1024} KB smaller.");
return 0;

static IEnumerable<string> Files(string root, params (string Folder, string Pattern)[] sets) =>
    sets.Select(set => (Path: Path.Combine(root, set.Folder), set.Pattern))
        .Where(set => Directory.Exists(set.Path))
        .SelectMany(set => Directory.EnumerateFiles(set.Path, set.Pattern));

static long Keep(string file, string source, string minified)
{
    if (minified.Length == 0 || minified.Length >= source.Length)
    {
        return 0;
    }

    File.WriteAllText(file, minified);
    return source.Length - minified.Length;
}

static void Warn(string file, string problem) => Console.WriteLine($"warning: {Path.GetFileName(file)} stays as it is: {problem}");

// The binary the build already pinned and downloaded (src/DotNetForge.UI/build/Tailwind.targets).
static string? FindTailwind()
{
    if (Environment.GetEnvironmentVariable("DNF_TAILWIND_PATH") is { Length: > 0 } configured)
    {
        return configured;
    }

    for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
    {
        var tools = Path.Combine(folder.FullName, "tools", "tailwind");
        if (Directory.Exists(tools))
        {
            return Directory.EnumerateFiles(tools, "tailwindcss-*", SearchOption.AllDirectories).FirstOrDefault();
        }
    }

    return null;
}
