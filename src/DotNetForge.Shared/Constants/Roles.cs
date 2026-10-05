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

    /// <summary>Roles permitted to enter the admin area (admin-capable roles).</summary>
    public static readonly IReadOnlyList<string> AdminCapable = new[]
    {
        SuperAdmin, Admin, Editor, Author,
    };
}
