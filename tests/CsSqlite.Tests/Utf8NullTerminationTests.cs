using System.Buffers;

namespace CsSqlite.Tests;

// Regression tests for issue #17: UTF-8 strings passed to C-string APIs must be
// NUL-terminated. PooledUtf8String rents from ArrayPool<byte>.Shared, whose buffers
// hold arbitrary bytes after the encoded text. The C-string consumers
// sqlite3_bind_parameter_index (named parameters) and sqlite3_open (database path)
// read until a NUL, so a missing terminator makes them read stale pooled bytes.
//
// The bug is intermittent because it depends on whatever happens to be in the rented
// buffer. To make it deterministic, each test first fills the relevant ArrayPool
// buckets with non-zero bytes ("pollution"); without the terminator the stale data is
// then guaranteed to be observed. These tests pass with the fix and fail without it.
public sealed class Utf8NullTerminationTests
{
    [Fact]
    public void NamedParameter_CharOverload_RoundTrips()
    {
        using var connection = new SqliteConnection(":memory:");
        connection.Open();
        connection.ExecuteNonQuery("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL);");

        using (var command = connection.CreateCommand("INSERT INTO t (id, name) VALUES (@id, @name);"))
        {
            command.Parameters.Add("@id", 1);
            command.Parameters.Add("@name", "Alice");
            command.ExecuteNonQuery();
        }

        using var reader = connection.ExecuteReader("SELECT name FROM t WHERE id = 1;");
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.False(reader.Read());
    }

    [Fact]
    public void NamedParameter_CharOverload_ResolvesWithDirtyArrayPool()
    {
        using var connection = new SqliteConnection(":memory:");
        connection.Open();
        connection.ExecuteNonQuery("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL);");

        using var command = connection.CreateCommand("INSERT INTO t (id, name) VALUES (@id, @name);");

        // Without the trailing NUL, sqlite3_bind_parameter_index reads the stale 0xFF bytes,
        // fails to match "@id"/"@name", returns index 0, and binding to 0 throws SQLITE_RANGE.
        PollutePool(0xFF);

        command.Parameters.Add("@id", 1);
        command.Parameters.Add("@name", "Alice");
        command.ExecuteNonQuery();

        using var reader = connection.ExecuteReader("SELECT id, name FROM t;");
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt(0));
        Assert.Equal("Alice", reader.GetString(1));
    }

    [Fact]
    public void Open_TargetsExactPath_WithDirtyArrayPool()
    {
        var dir = Directory.CreateTempSubdirectory("cssqlite_");
        try
        {
            var path = Path.Combine(dir.FullName, "exact.db");

            // Without the terminator, the stale 'X' bytes after the path would make
            // sqlite3_open open e.g. "exact.dbXXXX" instead of "exact.db".
            PollutePool((byte)'X');

            using (var connection = new SqliteConnection(path))
            {
                connection.Open();
                connection.ExecuteNonQuery("CREATE TABLE t (id INTEGER);");
                connection.ExecuteNonQuery("INSERT INTO t (id) VALUES (42);");
            }

            // The database must be created at exactly "exact.db". With the bug, sqlite3_open
            // reads the stale 'X' bytes after the path and opens e.g. "exact.dbXXX" instead,
            // so "exact.db" would never be created.
            Assert.True(File.Exists(path));

            // And the data round-trips when reopening the exact path.
            using var reopened = new SqliteConnection(path);
            using var reader = reopened.ExecuteReader("SELECT id FROM t;");
            Assert.True(reader.Read());
            Assert.Equal(42, reader.GetInt(0));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // Fill the small ArrayPool<byte>.Shared buckets with a non-zero byte and return them
    // (without clearing) so the next Rent reuses a buffer full of stale `fill` bytes.
    static void PollutePool(byte fill)
    {
        for (var size = 16; size <= 2048; size *= 2)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(size);
            buffer.AsSpan().Fill(fill);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
