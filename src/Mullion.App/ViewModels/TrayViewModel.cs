using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mullion.App.Services;

namespace Mullion.App.ViewModels;

public sealed partial class TrayViewModel(IAppHost host, Action showWindow, Action quit) : ObservableObject
{
    [ObservableProperty]
    private string _tooltip = "Mullion";

    [ObservableProperty]
    private bool _isPaused;

    public TrayViewModel() : this(new DesignAppHost(), () => { }, () => { }) { }

    [RelayCommand]
    private void ShowWindow() => showWindow();

    [RelayCommand]
    private void Rescan() => host.Rescan();

    [RelayCommand]
    private void TogglePause()
    {
        host.Paused = !host.Paused;
        IsPaused = host.Paused;
        Tooltip = IsPaused ? "Mullion — hotkeys paused" : "Mullion";
    }

    [RelayCommand]
    private void Quit() => quit();
}
