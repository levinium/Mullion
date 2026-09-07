using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Mullion.App.Controls;

/// <summary>
/// The monitor diagram together with the controls that reshape it, so the two
/// can be dropped into the main window and into settings as one thing.
/// </summary>
public partial class ZoneEditor : UserControl
{
    public ZoneEditor() => AvaloniaXamlLoader.Load(this);
}
