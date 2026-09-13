using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Mullion.App.ViewModels;

namespace Mullion.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        AvaloniaXamlLoader.Load(this);
        this.FitWhenOpened();

        // The window owns the file dialogs, not the view model. StorageProvider
        // needs a top-level to hang off, and a view model that reached for the
        // file system directly could not be run headless.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not SettingsViewModel vm) return;

            vm.SaveFileRequested = SaveAsync;
            vm.OpenFileRequested = OpenAsync;
        };
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

    private static readonly FilePickerFileType LayoutFile = new("Mullion layout")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
    };

    private async Task SaveAsync(string suggestedName, string contents)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export layout",
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = [LayoutFile],
        });

        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(contents);
    }

    /// <summary>Returns the file's contents, or null if the picker was dismissed.</summary>
    private async Task<string?> OpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import layout",
            AllowMultiple = false,
            FileTypeFilter = [LayoutFile],
        });

        if (files.Count == 0) return null;

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
