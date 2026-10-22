using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ComTest;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    public SettingsWindow() : this(new(new(Core.ConfigurationStore.DefaultPath), new(), "Default")) { }
    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.RefreshPortsAsync();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(null); };
    }
    private async void RefreshClicked(object? sender, RoutedEventArgs e) => await _viewModel.RefreshPortsAsync();
    private async void SaveClicked(object? sender, RoutedEventArgs e) => await _viewModel.SaveAsync();
    private async void LoadClicked(object? sender, RoutedEventArgs e) => await _viewModel.LoadAsync();
    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(null);
    private void ApplyClicked(object? sender, RoutedEventArgs e)
    {
        var result = _viewModel.BuildResult();
        if (result is not null) Close(result);
    }
}
