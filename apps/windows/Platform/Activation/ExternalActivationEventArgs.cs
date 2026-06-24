namespace Rynat.WindowsClient.Platform.Activation;

public sealed class ExternalActivationEventArgs : EventArgs
{
    public ExternalActivationEventArgs(IReadOnlyList<string> arguments)
    {
        Arguments = arguments;
    }

    public IReadOnlyList<string> Arguments { get; }
}
