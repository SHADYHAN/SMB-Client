namespace Rynat.WindowsClient.Platform.Activation;

public interface ILocalLinkRedirectService : IDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);

    void Stop();
}
