using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace ComTest;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _refreshTimer;
    private bool _shutdownComplete;
    private bool _shuttingDown;

    public MainWindow() : this(new(new(), new(Core.ConfigurationStore.DefaultPath))) { }

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        _refreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(75), DispatcherPriority.Background, (_, _) => RefreshBuffers());
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.HasError))
                StatusMessage.Foreground = viewModel.HasError ? Brush.Parse("#FF9B9B") : Brush.Parse("#91A2BC");
        };
        Opened += async (_, _) => { await viewModel.InitializeAsync(); _refreshTimer.Start(); };
        Closing += WindowClosing;
        Closed += (_, _) => _refreshTimer.Stop();
    }

    public void RefreshBuffers()
    {
        var incomingChanged = _viewModel.Incoming.Refresh(_viewModel.Session.InBuffer);
        var outgoingChanged = _viewModel.Outgoing.Refresh(_viewModel.Session.OutBuffer);
        if (_viewModel.AutoScroll)
        {
            if (incomingChanged) Dispatcher.UIThread.Post(IncomingPanel.FollowLatest, DispatcherPriority.Loaded);
            if (outgoingChanged) Dispatcher.UIThread.Post(OutgoingPanel.FollowLatest, DispatcherPriority.Loaded);
        }
    }

    private async void SettingsClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(new(_viewModel.Store, _viewModel.Profile, _viewModel.ProfileName));
        var result = await dialog.ShowDialog<SettingsResult?>(this);
        if (result is not null) _viewModel.ApplyProfile(result.Profile, result.ProfileName);
    }
    private async void OpenClicked(object? sender, RoutedEventArgs e) => await _viewModel.OpenPortAsync();
    private async void CloseClicked(object? sender, RoutedEventArgs e) => await _viewModel.ClosePortAsync();
    private async void SendClicked(object? sender, RoutedEventArgs e) => await _viewModel.SendAsync();
    private async void SendKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await _viewModel.SendAsync(); }
    }
    private void ClearIncoming(object? sender, EventArgs e) { _viewModel.Session.InBuffer.Clear(); RefreshBuffers(); }
    private void ClearOutgoing(object? sender, EventArgs e) { _viewModel.Session.OutBuffer.Clear(); RefreshBuffers(); }

    private async void WindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_shutdownComplete) return;
        e.Cancel = true;
        if (_shuttingDown) return;
        _shuttingDown = true;
        _refreshTimer.Stop();
        IsEnabled = false;
        try { await _viewModel.ShutdownAsync(); }
        catch (Exception ex) { _viewModel.Report($"Closing port: {ex.Message}", true); }
        finally { _shutdownComplete = true; Close(); }
    }
}
