namespace DotNetForge.Shared.Manifests;

/// <summary>A single manifest validation failure, tied to the offending field.</summary>
public sealed class ValidationError
{
    public ValidationError(string field, string message)
    {
        Field = field;
        Message = message;
    }

    /// <summary>PascalCase name of the offending field, e.g. <c>Version</c>, <c>Permissions</c>.</summary>
    public string Field { get; }

    public string Message { get; }

    public override string ToString() => $"{Field}: {Message}";
}

/// <summary>
/// The outcome of validating an <see cref="ExtensionManifest"/>. <see cref="IsValid"/> is true only
/// when <see cref="Errors"/> is empty.
/// </summary>
public sealed class ValidationResult
{
    private readonly List<ValidationError> _errors = new();

    public bool IsValid => _errors.Count == 0;

    public IReadOnlyList<ValidationError> Errors => _errors;

    public void Add(string field, string message) => _errors.Add(new ValidationError(field, message));

    public static ValidationResult Success() => new();
}
