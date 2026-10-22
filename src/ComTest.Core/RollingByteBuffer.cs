namespace ComTest.Core;

public sealed record BufferSnapshot(byte[] Data, long TotalBytes, long DroppedBytes, long Version);

/// <summary>A thread-safe byte ring that discards a block immediately on reaching capacity.</summary>
public sealed class RollingByteBuffer
{
    public const int Capacity = 10240;
    public const int DiscardSize = 512;
    private readonly byte[] _data = new byte[Capacity];
    private readonly object _sync = new();
    private int _head;
    private int _count;
    private long _total;
    private long _dropped;
    private long _version;

    public void Append(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return;
        lock (_sync)
        {
            _total += bytes.Length;
            while (!bytes.IsEmpty)
            {
                var tail = (_head + _count) % Capacity;
                var length = Math.Min(bytes.Length, Math.Min(Capacity - _count, Capacity - tail));
                bytes[..length].CopyTo(_data.AsSpan(tail));
                bytes = bytes[length..];
                _count += length;
                if (_count == Capacity)
                {
                    _head = (_head + DiscardSize) % Capacity;
                    _count -= DiscardSize;
                    _dropped += DiscardSize;
                }
            }
            _version++;
        }
    }

    public BufferSnapshot Snapshot()
    {
        lock (_sync)
        {
            var result = new byte[_count];
            var first = Math.Min(_count, Capacity - _head);
            _data.AsSpan(_head, first).CopyTo(result);
            _data.AsSpan(0, _count - first).CopyTo(result.AsSpan(first));
            return new(result, _total, _dropped, _version);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _head = _count = 0;
            _total = _dropped = 0;
            _version++;
        }
    }
}
