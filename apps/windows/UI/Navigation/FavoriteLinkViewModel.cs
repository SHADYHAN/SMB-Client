using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Navigation;

public sealed class FavoriteLinkViewModel : ObservableObject
{
    public FavoriteLinkViewModel(FavoriteLinkItem item)
    {
        Item = item;
    }

    public FavoriteLinkItem Item { get; }

    public string Name => Item.Name;

    public string Location => $"{Item.Share}{(Item.Path == "/" ? "" : Item.Path)}";

    public string Type => Item.IsDirectory ? "文件夹" : "文件";
}
