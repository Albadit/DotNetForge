namespace DotNetForge.Shared.Dtos;

/// <summary>The setup-wizard submission that creates the first Super Admin (.docs/features/installation.md).</summary>
public sealed class SetupRequest
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string ConfirmPassword { get; set; } = string.Empty;
}
