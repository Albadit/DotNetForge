using System.Diagnostics;
using System.IO.Compression;
using NUglify;

// Minifies a published wwwroot in place (.docs/guides/deployment.md): the screens' scripts and stylesheets, and the UI
// library's scripts; then writes the compressed copies the app serves. Development serves the sources as they are; only `dotnet publish` in Release runs this. Left alone:
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
    Console.WriteLine("warning: no Tailwind CSS CLI found (tools/node_modules), stylesheets are published as they are.");
}
else
{
    foreach (var stylesheet in Files(root, ("css", "*.css"), ("css/admin", "*.css")))
    {
        var output = Path.GetTempFileName();
        var process = Process.Start(new ProcessStartInfo("node", [tailwind, "--input", stylesheet, "--output", output, "--minify"])
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

// Compressed once here, at the strongest levels, instead of on every request (like Nuxt's compressPublicAssets): a .br
// and a .gz next to each text file, written after minification so they match it - the SDK's own copies are older.
// The app serves them as they are (src/DotNetForge.Web/Middleware/PrecompressedStaticFiles.cs).
var texts = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(IsText).ToList();
foreach (var file in texts)
{
    var content = File.ReadAllBytes(file);
    File.WriteAllBytes(file + ".br", Compress(content, output => new BrotliStream(output, CompressionLevel.SmallestSize)));
    File.WriteAllBytes(file + ".gz", Compress(content, output => new GZipStream(output, CompressionLevel.SmallestSize)));
}

Console.WriteLine($"Compressed {texts.Count} published text files (Brotli and gzip).");
return 0;

static bool IsText(string file) => Path.GetExtension(file) is ".css" or ".js" or ".mjs" or ".svg" or ".json";

static byte[] Compress(byte[] content, Func<Stream, Stream> compressor)
{
    using var output = new MemoryStream();
    using (var stream = compressor(output))
    {
        stream.Write(content);
    }

    return output.ToArray();
}

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

// The CLI the build already installed from npm (src/DotNetForge.UI/build/Tailwind.targets), run with node.
static string? FindTailwind()
{
    for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
    {
        var cli = Path.Combine(folder.FullName, "tools", "node_modules", "@tailwindcss", "cli", "dist", "index.mjs");
        if (File.Exists(cli))
        {
            return cli;
        }
    }

    return null;
}
