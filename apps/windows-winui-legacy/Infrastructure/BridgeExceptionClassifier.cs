using System.Runtime.InteropServices;
using System.Text.Json;
using Rynat.Client;

namespace Rynat.WindowsClient.Infrastructure;

public static class BridgeExceptionClassifier
{
    public static bool IsBridgeFailure(Exception exception) =>
        exception is RynatCoreBridgeException
            or DllNotFoundException
            or EntryPointNotFoundException
            or SEHException
            or JsonException;

    public static string ErrorCodeFor(Exception exception, string fallback = "bridge.failed") =>
        exception is RynatCoreBridgeException bridgeException
            ? bridgeException.ErrorCode ?? fallback
            : fallback;
}
