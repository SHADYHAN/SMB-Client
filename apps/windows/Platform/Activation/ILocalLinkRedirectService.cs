namespace Rynat.WindowsClient.Platform.Activation;

public interface ILocalLinkRedirectService : IDisposable
{
    event EventHandler<ExternalActivationEventArgs>? Activated;

    Task StartAsync(CancellationToken cancellationToken = default);

    void Stop();
}
