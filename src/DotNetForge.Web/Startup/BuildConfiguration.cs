using System.Reflection;

namespace DotNetForge.Web.Startup;

/// <summary>How this app was compiled. A Release build is what <c>dotnet publish</c> makes: it sends minified HTML and the
/// precompressed files of its publish output; a Debug build (<c>dotnet run</c>) sends everything as written
/// (.docs/guides/development.md#development-and-production-builds).</summary>
public static class BuildConfiguration
{
    public static bool IsRelease { get; } =
        typeof(BuildConfiguration).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration == "Release";
}
