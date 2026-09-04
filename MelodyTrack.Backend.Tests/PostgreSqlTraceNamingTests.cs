using MelodyTrack.Data.Telemetry;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public sealed class PostgreSqlTraceNamingTests
{
    [Theory]
    [InlineData("SELECT c.\"Id\" FROM \"Clients\" AS c", "SELECT Clients")]
    [InlineData("INSERT INTO public.\"AuditLogs\" (\"Id\") VALUES (@p0)", "INSERT public.AuditLogs")]
    [InlineData("UPDATE \"Users\" SET \"FirstName\" = @p0", "UPDATE Users")]
    [InlineData("DELETE FROM \"Sessions\" WHERE \"Id\" = @p0", "DELETE Sessions")]
    [InlineData("CREATE UNIQUE INDEX \"IX_Payments_ClientId\" ON \"Payments\" (\"ClientId\")", "CREATE INDEX Payments")]
    [InlineData("COMMIT", "COMMIT")]
    public void GetCommandSpanName_KnownCommand_ReturnsLowCardinalityLabel(string commandText, string expected)
    {
        var label = PostgreSqlTraceNaming.GetCommandSpanName(commandText);

        label.ShouldBe(expected);
    }

    [Fact]
    public void GetBatchSpanName_SameOperationAndTable_ReturnsSharedLabel()
    {
        var label = PostgreSqlTraceNaming.GetBatchSpanName(
        [
            "INSERT INTO \"Payments\" (\"Id\") VALUES (@p0)",
            "INSERT INTO \"Payments\" (\"Id\") VALUES (@p0)"
        ]);

        label.ShouldBe("BATCH INSERT Payments");
    }
}
