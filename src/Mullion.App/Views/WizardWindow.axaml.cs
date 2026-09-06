using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Mullion.App.Views;

public partial class WizardWindow : Window
{
    public WizardWindow() => AvaloniaXamlLoader.Load(this);
}
