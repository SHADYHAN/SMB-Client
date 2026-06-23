namespace Rynat.Client;

public sealed class RynatCoreBridgeException : InvalidOperationException
{
    public RynatCoreBridgeException(string message, string? errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }

    public string? ErrorCode { get; }
}
