namespace DotNetForge.Shared.Dtos;

/// <summary>What someone fills in to create an account: the model of the <c>_AccountFields</c> partial.</summary>
public interface IAccountFields
{
    string? FirstName { get; }

    string? LastName { get; }

    string? Email { get; }

    string? Password { get; }

    string? ConfirmPassword { get; }
}
