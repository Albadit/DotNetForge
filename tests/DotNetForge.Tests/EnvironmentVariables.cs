using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// Tests that change process environment variables (the configuration loader reads them) share this collection so
/// xUnit never runs them in parallel with each other.
/// </summary>
[CollectionDefinition(Collection, DisableParallelization = true)]
public sealed class EnvironmentVariables
{
    public const string Collection = "Environment variables";
}
