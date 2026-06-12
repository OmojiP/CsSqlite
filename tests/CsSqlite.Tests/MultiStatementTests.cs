namespace CsSqlite.Tests;

public sealed class MultiStatementTests
{
    [Fact]
    public void ExecuteNonQuery_CharOverload_ExecutesAllStatements()
    {
        using var connection = new SqliteConnection(":memory:");

        connection.ExecuteNonQuery("""
CREATE TABLE a (id INTEGER);
CREATE TABLE b (id INTEGER);
""");

        using var reader = connection.ExecuteReader("""
SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;
""");

        Assert.True(reader.Read());
        Assert.Equal("a", reader.GetString(0));
        Assert.True(reader.Read());
        Assert.Equal("b", reader.GetString(0));
        Assert.False(reader.Read());
    }

    [Fact]
    public void ExecuteNonQuery_Utf8Overload_ExecutesAllStatements()
    {
        using var connection = new SqliteConnection(":memory:");
        ReadOnlySpan<byte> sql = """
CREATE TABLE a (id INTEGER);
CREATE TABLE b (id INTEGER);
"""u8;

        connection.ExecuteNonQuery(sql);

        using var reader = connection.ExecuteReader("""
SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;
""");

        Assert.True(reader.Read());
        Assert.Equal("a", reader.GetString(0));
        Assert.True(reader.Read());
        Assert.Equal("b", reader.GetString(0));
        Assert.False(reader.Read());
    }

    [Fact]
    public void ExecuteNonQuery_IgnoresTrailingWhitespaceAndComments()
    {
        using var connection = new SqliteConnection(":memory:");

        connection.ExecuteNonQuery("""
CREATE TABLE a (id INTEGER);
-- trailing comment

""");

        using var reader = connection.ExecuteReader("SELECT name FROM sqlite_master WHERE type = 'table';");
        Assert.True(reader.Read());
        Assert.Equal("a", reader.GetString(0));
        Assert.False(reader.Read());
    }

    [Fact]
    public void ExecuteReader_SupportsNextResult()
    {
        using var connection = new SqliteConnection(":memory:");

        using var reader = connection.ExecuteReader("""
SELECT 1;
SELECT 2;
""");

        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt(0));
        Assert.False(reader.Read());

        Assert.True(reader.NextResult());
        Assert.True(reader.Read());
        Assert.Equal(2, reader.GetInt(0));
        Assert.False(reader.Read());
        Assert.False(reader.NextResult());
    }

    [Fact]
    public void Command_ExecutesAllStatements()
    {
        using var connection = new SqliteConnection(":memory:");

        using (var command = connection.CreateCommand("""
CREATE TABLE a (id INTEGER);
CREATE TABLE b (id INTEGER);
"""))
        {
            command.ExecuteNonQuery();
        }

        using var reader = connection.ExecuteReader("""
SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;
""");

        Assert.True(reader.Read());
        Assert.Equal("a", reader.GetString(0));
        Assert.True(reader.Read());
        Assert.Equal("b", reader.GetString(0));
        Assert.False(reader.Read());
    }

    [Fact]
    public void CommandReader_SupportsNextResult()
    {
        using var connection = new SqliteConnection(":memory:");

        using var command = connection.CreateCommand("""
SELECT 1;
SELECT 2;
""");
        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt(0));
        Assert.False(reader.Read());

        Assert.True(reader.NextResult());
        Assert.True(reader.Read());
        Assert.Equal(2, reader.GetInt(0));
        Assert.False(reader.Read());
        Assert.False(reader.NextResult());
    }
}
