using System.Collections.ObjectModel;
using System.IO.Ports;
using ComTest.Core;

namespace ComTest;

public sealed record SettingsResult(AppProfile Profile, string ProfileName);

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ConfigurationStore _store;
    private string _profileName;
    private string _portName = "";
    private string _baudRate = "115200";
    private int _dataBits = 8;
    private Parity _parity;
    private StopBits _stopBits = StopBits.One;
    private Handshake _handshake;
    private bool _dtrEnable;
    private bool _rtsEnable;
    private SendMode _sendMode;
    private LineEnding _lineEnding;
    private bool _autoScroll = true;
    private string _message = "Apply uses these settings for this instance. Save stores the named profile.";
    private bool _busy;

    public SettingsViewModel(ConfigurationStore store, AppProfile profile, string name)
    {
        _store = store; _profileName = name;
        Populate(profile);
    }
    public string ConfigPath => _store.FilePath;
    public ObservableCollection<string> Ports { get; } = [];
    public string[] BaudRateOptions { get; } = ["1200", "2400", "9600", "19200", "384000", "57600", "115200"];
    public int[] DataBitOptions { get; } = [5, 6, 7, 8];
    public Parity[] ParityOptions { get; } = Enum.GetValues<Parity>();
    public StopBits[] StopBitOptions { get; } = [StopBits.One, StopBits.OnePointFive, StopBits.Two];
    public Handshake[] HandshakeOptions { get; } = Enum.GetValues<Handshake>();
    public SendMode[] SendModeOptions { get; } = Enum.GetValues<SendMode>();
    public LineEnding[] LineEndingOptions { get; } = Enum.GetValues<LineEnding>();
    public string ProfileName { get => _profileName; set => Set(ref _profileName, value); }
    public string PortName { get => _portName; set => Set(ref _portName, value); }
    public string BaudRate { get => _baudRate; set => Set(ref _baudRate, value); }
    public int DataBits { get => _dataBits; set => Set(ref _dataBits, value); }
    public Parity Parity { get => _parity; set => Set(ref _parity, value); }
    public StopBits StopBits { get => _stopBits; set => Set(ref _stopBits, value); }
    public Handshake Handshake { get => _handshake; set { Set(ref _handshake, value); Changed(nameof(CanSetRts)); } }
    public bool CanSetRts => Handshake is not (Handshake.RequestToSend or Handshake.RequestToSendXOnXOff);
    public bool DtrEnable { get => _dtrEnable; set => Set(ref _dtrEnable, value); }
    public bool RtsEnable { get => _rtsEnable; set => Set(ref _rtsEnable, value); }
    public SendMode SendMode { get => _sendMode; set => Set(ref _sendMode, value); }
    public LineEnding LineEnding { get => _lineEnding; set => Set(ref _lineEnding, value); }
    public bool AutoScroll { get => _autoScroll; set => Set(ref _autoScroll, value); }
    public string Message { get => _message; set => Set(ref _message, value); }
    public bool IsAvailable => !_busy;

    public async Task RefreshPortsAsync()
    {
        try
        {
            var ports = await Task.Run(SerialTransport.GetPortNames);
            Ports.Clear();
            foreach (var port in ports) Ports.Add(port);
            if (ports.Length == 0) Message = "No ports detected. You can enter a port path manually, then refresh after connecting your adapter.";
        }
        catch (Exception ex) { Message = $"Cannot enumerate ports: {ex.Message}. You can still enter a port manually."; }
    }

    public SettingsResult? BuildResult()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ProfileName) || ProfileName.Trim().Length > 80)
                throw new ArgumentException("Enter a profile name of 1–80 characters.");
            if (!int.TryParse(BaudRate, out var baud)) throw new ArgumentException("Baud rate must be a whole number.");
            var profile = new AppProfile
            {
                SerialPort = new() { PortName = PortName.Trim(), BaudRate = baud, DataBits = DataBits, Parity = Parity,
                    StopBits = StopBits, Handshake = Handshake, DtrEnable = DtrEnable, RtsEnable = RtsEnable },
                Sending = new() { Mode = SendMode, LineEnding = LineEnding },
                Display = new() { AutoScroll = AutoScroll }
            };
            profile.Validate();
            return new(profile, ProfileName.Trim());
        }
        catch (Exception ex) { Message = ex.Message; return null; }
    }

    public async Task SaveAsync()
    {
        var result = BuildResult();
        if (result is null || _busy) return;
        _busy = true; Changed(nameof(IsAvailable));
        try { await _store.SaveAsync(result.ProfileName, result.Profile); Message = $"Saved profile “{result.ProfileName}”."; }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _busy = false; Changed(nameof(IsAvailable)); }
    }

    public async Task LoadAsync()
    {
        if (_busy) return;
        _busy = true; Changed(nameof(IsAvailable));
        try
        {
            var profile = await _store.LoadAsync(ProfileName);
            if (profile is null) Message = $"No saved profile named “{ProfileName}”.";
            else { Populate(profile); Message = $"Loaded profile “{ProfileName}”. Apply to use it."; }
        }
        catch (Exception ex) { Message = $"Could not load: {ex.Message}"; }
        finally { _busy = false; Changed(nameof(IsAvailable)); }
    }

    private void Populate(AppProfile profile)
    {
        var serial = profile.SerialPort;
        PortName = serial.PortName; BaudRate = serial.BaudRate.ToString(); DataBits = serial.DataBits;
        Parity = serial.Parity; StopBits = serial.StopBits; Handshake = serial.Handshake;
        DtrEnable = serial.DtrEnable; RtsEnable = serial.RtsEnable;
        SendMode = profile.Sending.Mode; LineEnding = profile.Sending.LineEnding; AutoScroll = profile.Display.AutoScroll;
    }
}
