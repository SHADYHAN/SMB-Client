using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Rynat.WindowsClient.UI.Controls;

public sealed class ResizeSplitter : Grid
{
    public ResizeSplitter()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }
}
