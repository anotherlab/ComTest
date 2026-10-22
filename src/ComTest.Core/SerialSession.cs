namespace ComTest.Core;

public enum ConnectionState { Closed, Opening, Open, Closing }

/// <summary>Owns a single port; blocking driver calls run away from the UI thread.</summary>
public sealed class SerialSession(Func<ISerialTransport>? transportFactory = null) : IAsyncDisposable
{
    private readonly Func<ISerialTransport> _transportFactory = transportFactory ?? (() => new SerialTransport());
    private readonly SemaphoreSlim _operations = new(1, 1);
    private volatile Connection? _connection;
    private volatile ConnectionState _state;
    private string? _lastError;

    public RollingByteBuffer InBuffer { get; } = new();
    public RollingByteBuffer OutBuffer { get; } = new();
    public ConnectionState State => _state;
    public string? LastError => _lastError;
    public event EventHandler? StateChanged;

    public async Task OpenAsync(SerialPortSettings settings)
    {
        settings.Validate();
        await _operations.WaitAsync();
        try
        {
            if (_connection is not null) throw new InvalidOperationException("A serial port is already open.");
            SetState(ConnectionState.Opening);
            var transport = _transportFactory();
            try { await Task.Run(() => transport.Open(settings)); }
            catch { transport.Dispose(); throw; }
            var connection = new Connection(transport);
            _connection = connection;
            SetState(ConnectionState.Open);
            connection.Reader = Task.Run(() => Receive(connection));
            _ = ObserveReaderAsync(connection);
        }
        catch (Exception ex)
        {
            if (_connection is null) SetState(ConnectionState.Closed, ex.Message);
            throw;
        }
        finally { _operations.Release(); }
    }

    public async Task<int> SendAsync(byte[] bytes, CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var connection = _connection;
            if (connection is null || _state != ConnectionState.Open)
                throw new InvalidOperationException("Open a serial port before sending data.");
            var sent = 0;
            while (sent < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                connection.Stop.Token.ThrowIfCancellationRequested();
                var count = Math.Min(256, bytes.Length - sent);
                var offset = sent;
                // Record only chunks the driver has successfully accepted.
                await Task.Run(() => connection.Transport.Write(bytes, offset, count));
                OutBuffer.Append(bytes.AsSpan(sent, count));
                sent += count;
            }
            return sent;
        }
        finally { _operations.Release(); }
    }

    public async Task CloseAsync()
    {
        // Interrupt a long multi-chunk send before waiting for the lifecycle gate.
        _connection?.RequestStop();
        await _operations.WaitAsync();
        try
        {
            var connection = _connection;
            if (connection is null) return;
            SetState(ConnectionState.Closing);
            _connection = null;
            try { await ReleaseAsync(connection); SetState(ConnectionState.Closed); }
            catch (Exception ex) { SetState(ConnectionState.Closed, ex.Message); throw; }
        }
        finally { _operations.Release(); }
    }

    private Exception? Receive(Connection connection)
    {
        var chunk = new byte[1024];
        while (!connection.Stop.IsCancellationRequested)
        {
            try
            {
                var count = connection.Transport.Read(chunk, 0, chunk.Length);
                if (count <= 0) throw new IOException("The serial port stopped returning data.");
                InBuffer.Append(chunk.AsSpan(0, count));
            }
            catch (TimeoutException) { }
            catch (Exception ex) { return connection.Stop.IsCancellationRequested ? null : ex; }
        }
        return null;
    }

    private async Task ObserveReaderAsync(Connection connection)
    {
        var error = await connection.Reader;
        await _operations.WaitAsync();
        try
        {
            if (_connection != connection) return;
            _connection = null;
            try { await ReleaseAsync(connection); }
            catch (Exception ex) { error ??= ex; }
            SetState(ConnectionState.Closed, error?.Message);
        }
        finally { _operations.Release(); }
    }

    private static async Task ReleaseAsync(Connection connection)
    {
        connection.RequestStop();
        try { await Task.Run(connection.Transport.Dispose); }
        finally
        {
            await connection.Reader;
            connection.Stop.Dispose();
        }
    }

    private void SetState(ConnectionState state, string? error = null)
    {
        _lastError = error;
        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync() => await CloseAsync();

    private sealed class Connection(ISerialTransport transport)
    {
        public ISerialTransport Transport { get; } = transport;
        public CancellationTokenSource Stop { get; } = new();
        public Task<Exception?> Reader { get; set; } = Task.FromResult<Exception?>(null);
        public void RequestStop()
        {
            try { Stop.Cancel(); }
            catch (ObjectDisposedException) { /* Another close already finished. */ }
        }
    }
}
