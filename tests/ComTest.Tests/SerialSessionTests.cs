using System.Collections.Concurrent;
using ComTest.Core;
using Xunit;

namespace ComTest.Tests;

public sealed class SerialSessionTests
{
    [Fact]
    public async Task TwoIndependentInstancesExchangeDataInBothDirectionsAndCanReopen()
    {
        var wireA = new BlockingCollection<byte[]>();
        var wireB = new BlockingCollection<byte[]>();
        await using var a = new SerialSession(() => new FakeTransport(wireA, wireB));
        await using var b = new SerialSession(() => new FakeTransport(wireB, wireA));
        await Task.WhenAll(a.OpenAsync(new() { PortName = "A" }), b.OpenAsync(new() { PortName = "B" }));
        var first = Enumerable.Range(0, 12000).Select(i => (byte)(i % 251)).ToArray();
        var second = new byte[] { 0, 10, 13, 32, 65, 127, 255 };
        await Task.WhenAll(a.SendAsync(first, TestContext.Current.CancellationToken), b.SendAsync(second, TestContext.Current.CancellationToken));
        await WaitForAsync(() => b.InBuffer.Snapshot().TotalBytes == first.Length && a.InBuffer.Snapshot().TotalBytes == second.Length);
        Assert.Equal(a.OutBuffer.Snapshot().Data, b.InBuffer.Snapshot().Data);
        Assert.Equal(b.OutBuffer.Snapshot().Data, a.InBuffer.Snapshot().Data);
        await Task.WhenAll(a.CloseAsync(), b.CloseAsync()).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionState.Closed, a.State);
        Assert.NotEmpty(a.InBuffer.Snapshot().Data);
        await a.OpenAsync(new() { PortName = "A" });
        Assert.Equal(ConnectionState.Open, a.State);
    }

    [Fact]
    public async Task FailedOpenReleasesTheTransportAndAllowsRetry()
    {
        var failed = new FakeTransport(new(), new()) { FailOpen = true };
        var attempts = 0;
        await using var session = new SerialSession(() => ++attempts == 1 ? failed : new FakeTransport(new(), new()));
        await Assert.ThrowsAsync<IOException>(() => session.OpenAsync(new() { PortName = "A" }));
        Assert.True(failed.Disposed);
        Assert.Equal(ConnectionState.Closed, session.State);
        await session.OpenAsync(new() { PortName = "A" });
        Assert.Equal(ConnectionState.Open, session.State);
    }

    [Fact]
    public async Task AFailedWriteOnlyRecordsThePreviouslySuccessfulChunks()
    {
        var transport = new FakeTransport(new(), new()) { FailWriteNumber = 2 };
        await using var session = new SerialSession(() => transport);
        await session.OpenAsync(new() { PortName = "A" });
        await Assert.ThrowsAsync<IOException>(() => session.SendAsync(new byte[600], TestContext.Current.CancellationToken));
        Assert.Equal(256, session.OutBuffer.Snapshot().TotalBytes);
    }

    [Fact]
    public async Task DeviceRemovalClosesTheConnectionAndReportsTheFault()
    {
        var transport = new FakeTransport(new(), new());
        await using var session = new SerialSession(() => transport);
        await session.OpenAsync(new() { PortName = "A" });
        transport.FailRead = true;
        await WaitForAsync(() => session.State == ConnectionState.Closed);
        Assert.Contains("removed", session.LastError);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public async Task ConcurrentClosesAreSafeEvenWhenTheReaderFails()
    {
        var transport = new FakeTransport(new(), new());
        await using var session = new SerialSession(() => transport);
        await session.OpenAsync(new() { PortName = "A" });
        transport.FailRead = true;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => session.CloseAsync()));
        Assert.Equal(ConnectionState.Closed, session.State);
        Assert.True(transport.Disposed);
    }

    internal static async Task WaitForAsync(Func<bool> predicate)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
}

internal sealed class FakeTransport(BlockingCollection<byte[]> input, BlockingCollection<byte[]> output) : ISerialTransport
{
    private int _writes;
    public bool FailOpen { get; init; }
    public volatile bool FailRead;
    public int FailWriteNumber { get; init; }
    public bool Disposed { get; private set; }
    public void Open(SerialPortSettings settings)
    {
        if (FailOpen) throw new IOException("Port is busy.");
    }
    public int Read(byte[] buffer, int offset, int count)
    {
        if (FailRead) throw new IOException("Device removed.");
        if (Disposed) throw new ObjectDisposedException(nameof(FakeTransport));
        if (!input.TryTake(out var chunk, 50)) throw new TimeoutException();
        chunk.CopyTo(buffer, offset);
        return chunk.Length;
    }
    public void Write(byte[] buffer, int offset, int count)
    {
        if (++_writes == FailWriteNumber) throw new IOException("Write failed.");
        output.Add(buffer.AsSpan(offset, count).ToArray());
    }
    public void Dispose() => Disposed = true;
}
