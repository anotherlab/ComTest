using Avalonia.Threading;
using ComTest.Core;

namespace ComTest;

public sealed class BufferViewModel(string title, string direction, string emptyText) : ObservableObject
{
    private long _version = -1;
    public string Title { get; } = title;
    public string Direction { get; } = direction;
    public string EmptyText { get; } = emptyText;
    public string Offsets { get; private set; } = "";
    public string Hex { get; private set; } = "";
    public string Ascii { get; private set; } = "";
    public int Count { get; private set; }
    public bool IsEmpty => Count == 0;
    public string CountLabel => $"{Count:N0} / 5,000 bytes";
    public string Totals { get; private set; } = "0 bytes total  ·  0 discarded";
    public bool Refresh(RollingByteBuffer buffer)
    {
        var snapshot = buffer.Snapshot();
        if (snapshot.Version == _version) return false;
        _version = snapshot.Version;
        var display = DataCodec.Format(snapshot);
        Offsets = display.Offsets; Hex = display.Hex; Ascii = display.Ascii;
        Count = snapshot.Data.Length;
        Totals = $"{snapshot.TotalBytes:N0} bytes total  ·  {snapshot.DroppedBytes:N0} discarded";
        foreach (var name in new[] { nameof(Offsets), nameof(Hex), nameof(Ascii), nameof(Count), nameof(IsEmpty), nameof(CountLabel), nameof(Totals) }) Changed(name);
        return true;
    }
}

public sealed class MainViewModel : ObservableObject
{
    private AppProfile _profile = new();
    private string _profileName;
    private string _sendText = "";
    private string _message = "Select a port in Settings to begin.";
    private bool _hasError;
    private bool _busy;
    private bool _autoScroll = true;
    private readonly string? _startupError;

    public MainViewModel(SerialSession session, ConfigurationStore store, string profileName = "Default", string? startupError = null)
    {
        Session = session; Store = store; _profileName = profileName; _startupError = startupError;
        Session.StateChanged += OnStateChanged;
    }

    public SerialSession Session { get; }
    public ConfigurationStore Store { get; }
    public BufferViewModel Incoming { get; } = new("Incoming", "RX", "Waiting for incoming data");
    public BufferViewModel Outgoing { get; } = new("Outgoing", "TX", "Your sent data will appear here");
    public AppProfile Profile => _profile with { Display = new() { AutoScroll = AutoScroll } };
    public string ProfileName => _profileName;
    public string WindowTitle => $"ComTest · {ProfileName} · {Environment.ProcessId}";
    public string InstanceLabel => $"Profile: {ProfileName}  ·  Instance {Environment.ProcessId}";
    public string PortSummary => _profile.SerialPort.Summary;
    public string SendSummary => $"{_profile.Sending.Mode}  ·  {_profile.Sending.LineEnding} line ending";
    public bool IsOpen => Session.State == ConnectionState.Open;
    public bool CanConfigure => !_busy && Session.State == ConnectionState.Closed;
    public bool CanOpen => CanConfigure && !string.IsNullOrWhiteSpace(_profile.SerialPort.PortName);
    public bool CanClose => IsOpen && !_busy;
    public bool CanSend => IsOpen && !_busy && SendText.Length > 0;
    public string StateLabel => Session.State switch
    {
        ConnectionState.Open => "CONNECTED",
        ConnectionState.Opening => "CONNECTING",
        ConnectionState.Closing => "CLOSING",
        _ => "DISCONNECTED"
    };
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool HasError { get => _hasError; private set => Set(ref _hasError, value); }
    public bool AutoScroll { get => _autoScroll; set => Set(ref _autoScroll, value); }
    public string SendText { get => _sendText; set { if (Set(ref _sendText, value)) Changed(nameof(CanSend)); } }

    public async Task InitializeAsync()
    {
        if (_startupError is not null) { Report(_startupError, true); return; }
        try
        {
            var profile = await Store.LoadAsync(ProfileName);
            if (profile is not null) ApplyProfile(profile, ProfileName);
        }
        catch (Exception ex) { Report($"Could not load settings: {ex.Message}", true); }
    }

    public void ApplyProfile(AppProfile profile, string name)
    {
        _profile = profile; _profileName = name.Trim(); AutoScroll = profile.Display.AutoScroll;
        foreach (var property in new[] { nameof(ProfileName), nameof(WindowTitle), nameof(InstanceLabel), nameof(PortSummary), nameof(SendSummary) }) Changed(property);
        RefreshState();
        Report("Settings applied. Open the port when ready.");
    }

    public async Task OpenPortAsync()
    {
        if (!CanOpen) return;
        await RunAsync(async () => { await Session.OpenAsync(_profile.SerialPort); Report($"Listening on {_profile.SerialPort.PortName}."); });
    }

    public async Task ClosePortAsync()
    {
        if (!CanClose) return;
        await RunAsync(async () => { await Session.CloseAsync(); Report("Port closed. Captured data is kept until cleared."); });
    }

    public async Task SendAsync()
    {
        if (!CanSend) return;
        await RunAsync(async () =>
        {
            var bytes = DataCodec.Encode(SendText, _profile.Sending);
            if (bytes.Length == 0) throw new ArgumentException("Enter at least one byte to send.");
            var sent = await Session.SendAsync(bytes);
            Report($"Sent {sent:N0} bytes to {_profile.SerialPort.PortName}.");
        });
    }

    private async Task RunAsync(Func<Task> operation)
    {
        _busy = true; RefreshState();
        try { await operation(); }
        catch (OperationCanceledException) { Report("Operation canceled; already transmitted bytes remain in OutBuffer."); }
        catch (Exception ex) { Report(ex.Message, true); }
        finally { _busy = false; RefreshState(); }
    }

    public void Report(string message, bool error = false) { Message = message; HasError = error; }
    private void OnStateChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        RefreshState();
        if (Session.LastError is { } error) Report($"Connection error: {error}", true);
    });
    private void RefreshState()
    {
        foreach (var name in new[] { nameof(IsOpen), nameof(CanConfigure), nameof(CanOpen), nameof(CanClose), nameof(CanSend), nameof(StateLabel) }) Changed(name);
    }
    public async Task ShutdownAsync()
    {
        Session.StateChanged -= OnStateChanged;
        await Session.DisposeAsync();
    }
}
