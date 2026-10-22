using System.IO.Ports;
using System.Text.Json.Serialization;

namespace ComTest.Core;

public enum SendMode { ASCII, UTF8, Hex }
public enum LineEnding { None, LF, CR, CRLF }

public sealed record SerialPortSettings
{
    public string PortName { get; init; } = "";
    public int BaudRate { get; init; } = 115200;
    public int DataBits { get; init; } = 8;
    public Parity Parity { get; init; } = Parity.None;
    public StopBits StopBits { get; init; } = StopBits.One;
    public Handshake Handshake { get; init; } = Handshake.None;
    public bool DtrEnable { get; init; }
    public bool RtsEnable { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PortName)) throw new ArgumentException("Select or enter a serial port.");
        if (BaudRate is < 1 or > 4000000) throw new ArgumentException("Baud rate must be between 1 and 4,000,000.");
        if (DataBits is < 5 or > 8) throw new ArgumentException("Data bits must be between 5 and 8.");
        if (!Enum.IsDefined(Parity)) throw new ArgumentException("Select a valid parity.");
        if (!Enum.IsDefined(StopBits) || StopBits == StopBits.None) throw new ArgumentException("Select valid stop bits.");
        if (!Enum.IsDefined(Handshake)) throw new ArgumentException("Select a valid flow control option.");
    }

    [JsonIgnore]
    public string Summary => string.IsNullOrWhiteSpace(PortName)
        ? "No port selected"
        : $"{PortName}  ·  {BaudRate:N0} baud  ·  {DataBits}{Parity.ToString()[0]}{StopBits switch { StopBits.One => "1", StopBits.Two => "2", _ => "1.5" }}";
}

public sealed record SendingSettings
{
    public SendMode Mode { get; init; } = SendMode.ASCII;
    public LineEnding LineEnding { get; init; } = LineEnding.None;
    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || !Enum.IsDefined(LineEnding))
            throw new ArgumentException("Select a valid send encoding and line ending.");
    }
}

public sealed record DisplaySettings
{
    public bool AutoScroll { get; init; } = true;
}

public sealed record AppProfile
{
    public SerialPortSettings SerialPort { get; init; } = new();
    public SendingSettings Sending { get; init; } = new();
    public DisplaySettings Display { get; init; } = new();

    public void Validate()
    {
        if (SerialPort is null || Sending is null || Display is null)
            throw new ArgumentException("The profile must contain SerialPort, Sending, and Display sections.");
        SerialPort.Validate();
        Sending.Validate();
    }
}
