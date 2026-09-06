using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Mullion.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Whether closing hides the window instead of exiting.
    /// <para>
    /// Only ever true when a tray icon exists to bring it back. Hiding with no
    /// tray leaves the app running and unreachable, with no way to quit short
    /// of Task Manager.
    /// </para>
    /// </summary>
    public bool HideInsteadOfClosing { get; set; }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (HideInsteadOfClosing && !e.IsProgrammatic)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
