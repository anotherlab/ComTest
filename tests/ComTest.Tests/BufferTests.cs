using ComTest.Core;
using Xunit;

namespace ComTest.Tests;

public sealed class BufferTests
{
    [Theory]
    [InlineData(4999, 4999, 0)]
    [InlineData(5000, 4488, 512)]
    [InlineData(5511, 4999, 512)]
    [InlineData(5512, 4488, 1024)]
    [InlineData(25000, 4520, 20480)]
    public void DiscardsExactlyAtTheThreshold(int inputLength, int retained, int dropped)
    {
        var bytes = Enumerable.Range(0, inputLength).Select(i => (byte)(i % 251)).ToArray();
        var buffer = new RollingByteBuffer();
        buffer.Append(bytes);
        var snapshot = buffer.Snapshot();
        Assert.Equal(retained, snapshot.Data.Length);
        Assert.Equal(dropped, snapshot.DroppedBytes);
        Assert.Equal(inputLength, snapshot.TotalBytes);
        Assert.Equal(bytes.Skip(dropped), snapshot.Data);
    }

    [Fact]
    public void ArbitraryReadBoundariesDoNotChangeTheRetainedBytes()
    {
        var random = new Random(1729);
        var bytes = new byte[182934];
        random.NextBytes(bytes);
        var buffer = new RollingByteBuffer();
        var reference = new Queue<byte>();
        foreach (var value in bytes)
        {
            reference.Enqueue(value);
            if (reference.Count == 5000) for (var i = 0; i < 512; i++) reference.Dequeue();
        }
        for (var i = 0; i < bytes.Length;)
        {
            var length = Math.Min(random.Next(1, 8192), bytes.Length - i);
            buffer.Append(bytes.AsSpan(i, length));
            i += length;
        }
        Assert.Equal(reference, buffer.Snapshot().Data);
    }

    [Fact]
    public async Task SnapshotsStayConsistentWhileReceiving()
    {
        var buffer = new RollingByteBuffer();
        var writer = Task.Run(() => { for (var i = 0; i < 10000; i++) buffer.Append(new byte[97]); }, TestContext.Current.CancellationToken);
        while (!writer.IsCompleted)
        {
            var snapshot = buffer.Snapshot();
            Assert.InRange(snapshot.Data.Length, 0, 4999);
            Assert.Equal(snapshot.TotalBytes, snapshot.Data.Length + snapshot.DroppedBytes);
            Assert.Equal(0, snapshot.DroppedBytes % 512);
        }
        await writer;
        var previous = buffer.Snapshot();
        buffer.Clear();
        var cleared = buffer.Snapshot();
        Assert.Empty(cleared.Data);
        Assert.Equal(0, cleared.TotalBytes);
        Assert.Equal(0, cleared.DroppedBytes);
        Assert.True(cleared.Version > previous.Version);
    }

    [Fact]
    public void BothDisplaysKeepEveryByteAndShowUnprintableBytesAsDots()
    {
        var result = DataCodec.Format(new([0, 9, 10, 13, 31, 32, 65, 126, 127, 255], 522, 512, 1));
        Assert.Equal("000200\n000208", result.Offsets);
        Assert.Equal("00 09 0A 0D 1F 20 41 7E\n7F FF", result.Hex);
        Assert.Equal("..... A~\n..", result.Ascii);
    }

    [Fact]
    public void EncodesTextHexAndLineEndingsWithoutSilentAsciiReplacement()
    {
        Assert.Equal(new byte[] { 72, 105, 13, 10 }, DataCodec.Encode("Hi", new() { LineEnding = LineEnding.CRLF }));
        Assert.Equal(new byte[] { 0, 127, 255 }, DataCodec.Encode("00 7F\nFF", new() { Mode = SendMode.Hex }));
        Assert.Equal(new byte[] { 0xC3, 0xA9 }, DataCodec.Encode("é", new() { Mode = SendMode.UTF8 }));
        Assert.Throws<ArgumentException>(() => DataCodec.Encode("é", new()));
        Assert.Throws<ArgumentException>(() => DataCodec.Encode("0 12", new() { Mode = SendMode.Hex }));
        Assert.Throws<ArgumentException>(() => DataCodec.Encode("GG", new() { Mode = SendMode.Hex }));
    }
}
