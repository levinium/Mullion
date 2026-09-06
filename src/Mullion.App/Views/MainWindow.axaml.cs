using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Mullion.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Closing hides rather than exits: Mullion lives in the tray, and closing
    /// the window is not a request to stop managing hotkeys.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!e.IsProgrammatic)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
