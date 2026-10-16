namespace NsdRpc;

/// <summary>
/// The outcome of one RPC call. A success holds a value. A failure holds a code.
/// </summary>
public readonly struct RpcResult<T>
{
    private readonly T _value;
    private readonly int _errorCode;
    private readonly string _errorMessage;

    private RpcResult(T value, int errorCode, string errorMessage)
    {
        _value = value;
        _errorCode = errorCode;
        _errorMessage = errorMessage;
    }

    /// <summary>True when the call succeeded.</summary>
    public bool IsSuccess => _errorCode == 0;

    /// <summary>The result value. The default value on failure.</summary>
    public T Value => _value;

    /// <summary>The error code. Zero on success.</summary>
    public int ErrorCode => _errorCode;

    /// <summary>The error message. Empty on success.</summary>
    public string ErrorMessage => _errorMessage ?? string.Empty;

    public static RpcResult<T> Success(T value)
    {
        return new RpcResult<T>(value, 0, string.Empty);
    }

    public static RpcResult<T> Failure(int errorCode, string errorMessage)
    {
        Invariant.That(errorCode != 0, "A failure needs a nonzero error code.");
        return new RpcResult<T>(default!, errorCode, errorMessage ?? string.Empty);
    }
}
