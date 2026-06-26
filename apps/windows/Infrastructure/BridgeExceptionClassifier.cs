using System.Runtime.InteropServices;
using System.Text.Json;
using Rynat.Client;

namespace Rynat.WindowsClient.Infrastructure;

public static class BridgeExceptionClassifier
{
    private const string ReconnectableErrorCode = "reconnectable";

    public static bool IsBridgeFailure(Exception exception) =>
        exception is RynatCoreBridgeException
                or DllNotFoundException
                or EntryPointNotFoundException
                or SEHException
                or JsonException
            || exception is AggregateException aggregateException
                && aggregateException.InnerExceptions.Any(IsBridgeFailure);

    public static string ErrorCodeFor(Exception exception, string fallback = "bridge.failed") =>
        exception switch
        {
            RynatCoreBridgeException bridgeException => bridgeException.ErrorCode ?? fallback,
            AggregateException aggregateException => aggregateException.InnerExceptions
                .Select(inner => ErrorCodeFor(inner, fallback))
                .FirstOrDefault(code => !string.Equals(code, fallback, StringComparison.OrdinalIgnoreCase)) ?? fallback,
            _ => fallback
        };

    public static bool IsReconnectable(Exception exception) =>
        exception switch
        {
            RynatCoreBridgeException bridgeException => IsReconnectableCode(bridgeException.ErrorCode),
            AggregateException aggregateException => aggregateException.InnerExceptions.Any(IsReconnectable),
            _ => false
        };

    public static bool IsReconnectableCode(string? errorCode) =>
        string.Equals(errorCode, ReconnectableErrorCode, StringComparison.OrdinalIgnoreCase);
}
