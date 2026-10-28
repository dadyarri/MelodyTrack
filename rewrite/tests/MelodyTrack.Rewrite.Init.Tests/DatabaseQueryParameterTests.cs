using MelodyTrack.Rewrite.Persistence;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class DatabaseQueryParameterTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Query_EmptySql_RejectsBeforeOpeningConnection(string sql)
    {
        await using var source = NpgsqlDataSource.Create("Host=localhost;Database=builder_tests;Username=builder_tests");
        var database = new Database(source);

        Assert.Throws<ArgumentException>(() => database.Query(sql));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("@")]
    [InlineData("1value")]
    [InlineData("some value")]
    public async Task WithParameter_InvalidName_RejectsBeforeOpeningConnection(string name)
    {
        await using var source = NpgsqlDataSource.Create("Host=localhost;Database=builder_tests;Username=builder_tests");
        var query = new Database(source).Query("SELECT @value");

        Assert.Throws<ArgumentException>(() => query.WithParameter(name, 1));
    }
}
