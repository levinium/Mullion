using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Mullion.App.ViewModels;

namespace Mullion.App.Views;

public partial class SponsorWindow : Window
{
    public SponsorWindow()
    {
        AvaloniaXamlLoader.Load(this);
        this.FitWhenOpened();

        // The window owns the browser, not the view model: a view model that
        // reached for the shell could not be run headless, and this is the one
        // action here worth being able to test without one opening.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not SponsorViewModel vm) return;

            vm.OpenRequested = Open;
            vm.CloseRequested = Close;
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.ClampToScreen();
    }

    /// <summary>
    /// Hand the link to whatever the machine uses for links.
    /// <para>
    /// UseShellExecute, so this is the browser someone chose rather than an
    /// attempt to find one. Failures are swallowed: a machine with no default
    /// browser is a machine where nothing useful can happen here, and throwing
    /// out of a donate button would be an absurd way to lose the app.
    /// </para>
    /// </summary>
    private static void Open(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Nothing to do and nothing worth interrupting anyone over.
        }
    }
}
