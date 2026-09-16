namespace KiloSessionHost;

internal sealed class ByteRingBuffer
{
    private readonly int _capacity;
    private readonly Queue<byte[]> _chunks = new();
    private readonly object _gate = new();
    private int _size;

    public ByteRingBuffer(int capacity)
    {
        _capacity = Math.Max(64 * 1024, capacity);
    }

    public void Append(byte[] data)
    {
        if (data.Length == 0) return;

        lock (_gate)
        {
            if (data.Length >= _capacity)
            {
                _chunks.Clear();
                _size = 0;
                var tail = new byte[_capacity];
                Buffer.BlockCopy(data, data.Length - _capacity, tail, 0, _capacity);
                _chunks.Enqueue(tail);
                _size = tail.Length;
                return;
            }

            var copy = new byte[data.Length];
            Buffer.BlockCopy(data, 0, copy, 0, data.Length);
            _chunks.Enqueue(copy);
            _size += copy.Length;

            while (_size > _capacity && _chunks.Count > 1)
            {
                _size -= _chunks.Dequeue().Length;
            }
        }
    }

    public byte[] Snapshot()
    {
        lock (_gate)
        {
            var result = new byte[_size];
            var offset = 0;
            foreach (var chunk in _chunks)
            {
                Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }
            return result;
        }
    }
}
