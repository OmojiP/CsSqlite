using System.Buffers;
using System.Text;

namespace CsSqlite;

internal ref struct PooledUtf8String
{
    byte[]? buffer;
    readonly int count;

    public PooledUtf8String(ReadOnlySpan<char> str)
    {
        // +1 byte for a trailing NUL: sqlite3_bind_parameter_index reads buffer as a C string.
        buffer = ArrayPool<byte>.Shared.Rent(str.Length * 3 + 1);
        count = Encoding.UTF8.GetBytes(str, buffer);
        buffer[count] = 0;
    }

    public ReadOnlySpan<byte> AsSpan() => buffer.AsSpan(0, count);

    public void Dispose()
    {
        if (buffer == null) return;
        ArrayPool<byte>.Shared.Return(buffer);
    }
}