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

/// <summary>A success/failure result carrying a value on success.</summary>
public sealed class Result<T> : Result
{
    private Result(bool succeeded, T? value, IReadOnlyList<string> errors)
        : base(succeeded, errors)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Ok(T value) => new(true, value, Array.Empty<string>());

    public static new Result<T> Fail(string error) => new(false, default, new[] { error });

    public static new Result<T> Fail(IReadOnlyList<string> errors) => new(false, default, errors);
}
