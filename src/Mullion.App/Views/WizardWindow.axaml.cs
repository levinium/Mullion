using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Mullion.App.Views;

public partial class WizardWindow : Window
{
    public WizardWindow()
    {
        AvaloniaXamlLoader.Load(this);
        this.FitWhenOpened();
    }

    /// <summary>
    /// Fitted to the screen before it is shown, so a size chosen on a big
    /// display does not hang off the bottom of a small one.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.ClampToScreen();
    }
}
