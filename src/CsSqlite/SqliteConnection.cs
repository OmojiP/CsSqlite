using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static CsSqlite.NativeMethods;

namespace CsSqlite;

public sealed unsafe class SqliteConnection(string path) : IDisposable
{
    enum State : byte
    {
        None,
        Open,
        Disposed,
    }

    State state;
    internal sqlite3* db;

    public bool IsDisposed => state == State.Disposed;

    public void Open()
    {
        ThrowIfDisposed();
        if (state == State.Open)
            return;

        // PooledUtf8String is NUL-terminated, which sqlite3_open requires as a C string.
        using var utf8Path = new PooledUtf8String(path);
        fixed (byte* fileNamePtr = utf8Path.AsSpan())
        fixed (sqlite3** p = &db)
        {
            var code = sqlite3_open(fileNamePtr, p);
            if (code != Constants.SQLITE_OK)
            {
                throw new SqliteException(code, "Could not open database file: " + path);
            }
        }

        state = State.Open;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SqliteCommand CreateCommand(ReadOnlySpan<byte> utf8CommandText)
    {
        ThrowIfDisposed();
        Open();
        return new SqliteCommand(this, PrepareAll(utf8CommandText));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SqliteCommand CreateCommand(ReadOnlySpan<char> commandText)
    {
        ThrowIfDisposed();
        Open();
        return new SqliteCommand(this, PrepareAll(commandText));
    }

    PreparedStatements PrepareAll(ReadOnlySpan<byte> utf8CommandText)
    {
        var stmts = ArrayPool<IntPtr>.Shared.Rent(4);
        var count = 0;
        fixed (byte* sql = utf8CommandText)
        {
            var current = sql;
            var end = sql + utf8CommandText.Length;

            while (current < end)
            {
                var remaining = (int)(end - current);
                sqlite3_stmt* stmt = default;
                byte* tail = default;

                var code = sqlite3_prepare_v2(db, current, remaining, &stmt, &tail);
                if (code != Constants.SQLITE_OK)
                {
                    var errmsg = sqlite3_errmsg(db);
                    var message = Marshal.PtrToStringAnsi((nint)errmsg);
                    FinalizeAndReturn(stmts, count);
                    throw new SqliteException(code, message);
                }

                if (stmt != null)
                {
                    AddStatement(ref stmts, ref count, stmt);
                }

                if (tail <= current)
                    break;
                current = tail;
            }
        }

        return new PreparedStatements(stmts, count);
    }

    PreparedStatements PrepareAll(ReadOnlySpan<char> commandText)
    {
        var stmts = ArrayPool<IntPtr>.Shared.Rent(4);
        var count = 0;
        fixed (char* sql = commandText)
        {
            var current = sql;
            var end = sql + commandText.Length;

            while (current < end)
            {
                var remainingBytes = (int)((end - current) * 2);
                sqlite3_stmt* stmt = default;
                void* tailPtr = default;

                var code = sqlite3_prepare16_v2(db, current, remainingBytes, &stmt, &tailPtr);
                if (code != Constants.SQLITE_OK)
                {
                    var errmsg = sqlite3_errmsg(db);
                    FinalizeAndReturn(stmts, count);
                    throw new SqliteException(code, Marshal.PtrToStringAnsi(new IntPtr(errmsg)));
                }

                if (stmt != null)
                {
                    AddStatement(ref stmts, ref count, stmt);
                }

                var tail = (char*)tailPtr;
                if (tail <= current)
                    break;
                current = tail;
            }
        }

        return new PreparedStatements(stmts, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ExecuteNonQuery(ReadOnlySpan<byte> utf8CommandText)
    {
        ThrowIfDisposed();
        Open();

        var stmts = PrepareAll(utf8CommandText);
        var count = 0;
        try
        {
            for (var i = 0; i < stmts.Count; i++)
            {
                try
                {
                    count += ExecutePreparedNonQuery((sqlite3_stmt*)stmts.Buffer[i]);
                }
                finally
                {
                    stmts.Buffer[i] = IntPtr.Zero;
                }
            }
        }
        finally
        {
            FinalizeAndReturn(stmts.Buffer, stmts.Count);
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ExecuteNonQuery(ReadOnlySpan<char> commandText)
    {
        ThrowIfDisposed();
        Open();

        var stmts = PrepareAll(commandText);
        var count = 0;
        try
        {
            for (var i = 0; i < stmts.Count; i++)
            {
                try
                {
                    count += ExecutePreparedNonQuery((sqlite3_stmt*)stmts.Buffer[i]);
                }
                finally
                {
                    stmts.Buffer[i] = IntPtr.Zero;
                }
            }
        }
        finally
        {
            FinalizeAndReturn(stmts.Buffer, stmts.Count);
        }

        return count;
    }

    int ExecutePreparedNonQuery(sqlite3_stmt* stmt)
    {
        try
        {
            var count = 0;
            while (true)
            {
                var code = sqlite3_step(stmt);
                switch (code)
                {
                    case Constants.SQLITE_DONE:
                        return count;
                    case Constants.SQLITE_ROW:
                        count++;
                        break;
                    case Constants.SQLITE_ERROR:
                        var errmsg = sqlite3_errmsg(db);
                        throw new SqliteException(code, Marshal.PtrToStringAnsi((nint)errmsg));
                    case Constants.SQLITE_MISUSE:
                        throw new SqliteException(code, "Invalid SQL statement");
                    default:
                        throw new SqliteException(code, "Could not execute SQL statement.");
                }
            }
        }
        finally
        {
            sqlite3_finalize(stmt);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SqliteReader ExecuteReader(ReadOnlySpan<byte> utf8CommandText)
    {
        ThrowIfDisposed();
        Open();
        var reader = new SqliteReader(this, PrepareAll(utf8CommandText), true, true);
        reader.NextResult();
        return reader;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SqliteReader ExecuteReader(ReadOnlySpan<char> commandText)
    {
        ThrowIfDisposed();
        Open();
        var reader = new SqliteReader(this, PrepareAll(commandText), true, true);
        reader.NextResult();
        return reader;
    }

    static void AddStatement(ref IntPtr[] statements, ref int count, sqlite3_stmt* stmt)
    {
        if (count == statements.Length)
        {
            var next = ArrayPool<IntPtr>.Shared.Rent(statements.Length * 2);
            statements.AsSpan(0, count).CopyTo(next);
            ArrayPool<IntPtr>.Shared.Return(statements, clearArray: true);
            statements = next;
        }

        statements[count++] = (nint)stmt;
    }

    static void FinalizeAndReturn(IntPtr[] statements, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (statements[i] != IntPtr.Zero)
            {
                sqlite3_finalize((sqlite3_stmt*)statements[i]);
            }
        }

        ArrayPool<IntPtr>.Shared.Return(statements, clearArray: true);
    }

    internal void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(SqliteConnection));
        }
    }

    public void Dispose()
    {
        ThrowIfDisposed();

        if (state == State.Open)
        {
            sqlite3_close(db);
            db = null;
        }

        state = State.Disposed;
    }
}
