namespace DotNetForge.Shared.Constants;

/// <summary>
/// The six canonical built-in roles, most-privileged to least. These names are canonical
/// and defined once here (owned by .docs/features/authorization.md).
/// </summary>
public static class Roles
{
    public const string SuperAdmin = "Super Admin";
    public const string Admin = "Admin";
    public const string Editor = "Editor";
    public const string Author = "Author";
    public const string Authenticated = "Authenticated";
    public const string Public = "Public";

    /// <summary>All built-in roles, ordered most-privileged to least.</summary>
    public static readonly IReadOnlyList<string> BuiltIn = new[]
    {
        SuperAdmin, Admin, Editor, Author, Authenticated, Public,
    };

    /// <summary>The built-in roles that may enter the admin area (admin-capable roles).</summary>
    public static readonly IReadOnlyList<string> AdminCapable = new[]
    {
        SuperAdmin, Admin, Editor, Author,
    };

    /// <summary>Roles that never give access to the admin area: site visitors and signed-in end users.</summary>
    public static readonly IReadOnlyList<string> SiteOnly = new[] { Authenticated, Public };

    /// <summary>
    /// Whether a user holding <paramref name="roles"/> may enter the admin area: any role except
    /// <see cref="SiteOnly"/> - the admin-capable built-ins and every custom role. What they can do there is decided
    /// by their permissions (.docs/features/authorization.md#screen-access).
    /// </summary>
    public static bool CanEnterAdmin(IEnumerable<string> roles) =>
        roles.Any(role => !string.IsNullOrWhiteSpace(role) && !SiteOnly.Contains(role, StringComparer.OrdinalIgnoreCase));
}
