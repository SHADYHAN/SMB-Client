namespace Rynat.WindowsClient.Platform.Activation;

public interface IAppSingleInstanceService : IDisposable
{
    event EventHandler<ExternalActivationEventArgs>? Activated;

    bool IsPrimaryInstance { get; }

    Task<bool> StartAsync(IReadOnlyList<string> startupArguments, CancellationToken cancellationToken = default);

    void Stop();
}
