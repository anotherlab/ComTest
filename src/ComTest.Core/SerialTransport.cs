using System.IO.Ports;

namespace ComTest.Core;

public interface ISerialTransport : IDisposable
{
    void Open(SerialPortSettings settings);
    int Read(byte[] buffer, int offset, int count);
    void Write(byte[] buffer, int offset, int count);
}

public sealed class SerialTransport : ISerialTransport
{
    private SerialPort? _port;

    public static string[] GetPortNames() => SerialPort.GetPortNames()
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    public void Open(SerialPortSettings settings)
    {
        settings.Validate();
        _port = new SerialPort(settings.PortName.Trim(), settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits)
        {
            Handshake = settings.Handshake,
            DtrEnable = settings.DtrEnable,
            ReadTimeout = 200,
            WriteTimeout = 2000
        };
        if (settings.Handshake is not (Handshake.RequestToSend or Handshake.RequestToSendXOnXOff))
            _port.RtsEnable = settings.RtsEnable;
        _port.Open();
    }

    public int Read(byte[] buffer, int offset, int count) => _port!.Read(buffer, offset, count);
    public void Write(byte[] buffer, int offset, int count) => _port!.Write(buffer, offset, count);
    public void Dispose() => _port?.Dispose();
}
