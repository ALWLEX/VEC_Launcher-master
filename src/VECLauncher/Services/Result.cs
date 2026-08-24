namespace VECLauncher.Services;

/// <summary>
/// Railway-oriented result type. Eliminates try/catch and null-checking
/// by making success/failure explicit in the type system.
/// </summary>
public readonly struct Result<T>
{
    /// <summary>The value if successful, default(T) if failed.</summary>
    public T? Value { get; }

    /// <summary>Error message if failed, null if successful.</summary>
    public string? Error { get; }

    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>Whether the operation failed.</summary>
    public bool IsFailure => Error is not null;

    private Result(T? value, string? error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>Creates a successful result with the given value.</summary>
    public static Result<T> Ok(T value) => new(value, null);

    /// <summary>Creates a failed result with the given error message.</summary>
    public static Result<T> Fail(string error) => new(default, error);

    /// <summary>Maps the value to a new type if successful.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map)
        => IsSuccess ? Result<TOut>.Ok(map(Value!)) : Result<TOut>.Fail(Error!);

    /// <summary> Chains another operation if successful (monadic bind). </summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind)
        => IsSuccess ? bind(Value!) : Result<TOut>.Fail(Error!);

    /// <summary>Returns the value or throws if failed.</summary>
    public T Unwrap()
    {
        if (IsFailure) throw new InvalidOperationException($"Result is failure: {Error}");
        return Value!;
    }

    /// <summary>Returns the value or the provided default.</summary>
    public T UnwrapOrDefault(T defaultValue)
        => IsSuccess ? Value! : defaultValue;

    /// <summary>Implicit conversion from value to Result.</summary>
    public static implicit operator Result<T>(T value) => Ok(value);

    public override string ToString()
        => IsSuccess ? $"Ok({Value})" : $"Fail({Error})";
}

/// <summary>
/// Non-generic result for operations that don't return a value.
/// </summary>
public readonly struct Result
{
    public string? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => Error is not null;

    private Result(string? error) => Error = error;

    /// <summary>Creates a successful result.</summary>
    public static Result Ok() => new(null);

    /// <summary>Creates a failed result with the given error message.</summary>
    public static Result Fail(string error) => new(error);

    /// <summary>Returns the value or throws if failed.</summary>
    public void Unwrap()
    {
        if (IsFailure) throw new InvalidOperationException($"Result is failure: {Error}");
    }

    public override string ToString()
        => IsSuccess ? "Ok()" : $"Fail({Error})";
}
