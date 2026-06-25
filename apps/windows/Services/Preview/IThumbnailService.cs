namespace Rynat.WindowsClient.Services.Preview;

public interface IThumbnailService
{
    bool TryCreateThumbnail(string sourcePath, string destinationPath, int maxEdgePx);
}
