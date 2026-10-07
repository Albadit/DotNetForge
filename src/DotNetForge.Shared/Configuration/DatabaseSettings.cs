namespace DotNetForge.Shared.Configuration;

/// <summary>
/// One configured database exactly as read from the environment (.docs/database/configuration.md). The database
/// layer resolves <see cref="Provider"/> through the provider registry and lets that provider validate and normalize
/// the rest; nothing here is interpreted by the loader.
/// </summary>
public sealed record DatabaseSettings
{
    /// <summary>Name of the database the CMS itself runs on.</summary>
    public const string MainName = "main";

    /// <summary><see cref="MainName"/>, or the &lt;NAME&gt; of <c>DATABASES_&lt;NAME&gt;_*</c> (lower case).</summary>
    public string Name { get; init; } = MainName;

    /// <summary>Registered provider name (<c>sqlite</c>, <c>postgresql</c>, <c>sqlserver</c>, <c>mysql</c>, <c>mongodb</c>, ...); <c>null</c> = detect from the connection string.</summary>
    public string? Provider { get; init; }

    /// <summary>Secret: may contain credentials. Never log or render it.</summary>
    public string? ConnectionString { get; init; }

    /// <summary>Not read from the environment: set by providers that need it separately (MongoDB, from the URL path).</summary>
    public string? DatabaseName { get; init; }

    public bool IsMain => string.Equals(Name, MainName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Never prints the connection string.</summary>
    public override string ToString() => $"{Name} ({Provider ?? "auto-detect"})";
}
