namespace CsSqlite;

internal readonly struct PreparedStatements(IntPtr[] buffer, int count)
{
    public readonly IntPtr[] Buffer = buffer;
    public readonly int Count = count;
}
