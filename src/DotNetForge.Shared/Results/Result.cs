namespace DotNetForge.Shared.Results;

/// <summary>A lightweight success/failure result with optional error messages.</summary>
public class Result
{
    protected Result(bool succeeded, IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        Errors = errors;
    }

    public bool Succeeded { get; }

    public bool Failed => !Succeeded;

    public IReadOnlyList<string> Errors { get; }

    public string Error => Errors.Count > 0 ? Errors[0] : string.Empty;

    public static Result Ok() => new(true, Array.Empty<string>());

    public static Result Fail(string error) => new(false, new[] { error });

    public static Result Fail(IReadOnlyList<string> errors) => new(false, errors);
}
