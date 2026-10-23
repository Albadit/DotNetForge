namespace DotNetForge.Shared.Constants;

/// <summary>
/// Names of the ASP.NET Core authorization policies registered in <c>Startup/DependencyRegistration.cs</c>
/// (.docs/features/authorization.md). Extensions use them to protect their own endpoints.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>
    /// Signed in with any role except the site-only ones (<see cref="Roles.CanEnterAdmin"/>) - the gate of <c>/admin</c>.
    /// Each screen then requires its own permission.
    /// </summary>
    public const string AdminArea = "AdminArea";
}
