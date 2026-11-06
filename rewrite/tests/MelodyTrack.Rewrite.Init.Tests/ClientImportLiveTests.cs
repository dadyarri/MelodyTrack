using MelodyTrack.Rewrite.Init.Import;
using MelodyTrack.Rewrite.Persistence.Clients;
using MelodyTrack.Rewrite.Persistence.Import;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class ClientImportLiveTests(PostgreSqlFixture fixture, ITestOutputHelper output) : IClassFixture<PostgreSqlFixture>
{
    [Fact(Explicit = true)]
    public async Task Run_ConfiguredOldDatabase_ReconcilesImportedClientsAndReplaysWithoutDuplicates()
    {
        var token = TestContext.Current.CancellationToken;
        var path = Environment.GetEnvironmentVariable("MELODYTRACK_IMPORT_SOURCE_CONFIG")
            ?? throw new InvalidOperationException("Для явной проверки требуется путь MELODYTRACK_IMPORT_SOURCE_CONFIG.");
        var configuration = new ConfigurationBuilder().AddJsonFile(path).AddEnvironmentVariables().Build();
        var sourceSettings = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("OldDatabase"))
        {
            Pooling = false, Multiplexing = false, IncludeErrorDetail = false
        };
        sourceSettings.Options = (sourceSettings.Options + " -c default_transaction_read_only=on").Trim();
        await using var source = new NpgsqlConnection(sourceSettings.ConnectionString);
        await source.OpenAsync(token);
        await using var target = new NpgsqlConnection(await fixture.CreateDatabaseAsync(token));
        await target.OpenAsync(token);
        await ClientImportTests.ApplySchema(target);
        using var legacy = new LegacyPersonalDataReader(configuration.GetSection("Import:LegacyKeys").GetChildren()
            .ToDictionary(child => child.Key, child => child.Value ?? "", StringComparer.Ordinal));
        using var protector = ClientImportTests.Protector();
        var fields = new ClientDataProtection(protector);

        var first = await ClientImport.RunAsync(source, target, legacy, fields, token);
        var replay = await ClientImport.RunAsync(source, target, legacy, fields, token);

        output.WriteLine("Source rows: sources={0}, clients={1}, contacts={2}. Inserted: sources={3}, clients={4}, contacts={5}. Replay matched: sources={6}, clients={7}.",
            first.SourceRows, first.ClientRows, first.ContactRows, first.SourcesInserted, first.ClientsInserted, first.ContactsInserted, replay.SourcesMatched, replay.ClientsMatched);
        foreach (var group in first.Issues.GroupBy(issue => issue.Code)) output.WriteLine("Warning: {0}, count={1}.", group.Key, group.Count());
        Assert.DoesNotContain(first.Issues, issue => issue.Code == "source_decryption_failed");
        Assert.DoesNotContain(first.Issues, issue => issue.Code == "target_client_differs");
        Assert.True(first.ClientsInserted > 0);
        Assert.Equal(0, replay.ClientsInserted);
        Assert.Equal(0, replay.ContactsInserted);
        Assert.Equal(first.ClientsInserted, replay.ClientsMatched);
        Assert.Equal(first.SourcesInserted, replay.SourcesMatched);
        Assert.Equal(first.Issues.Count, replay.Issues.Count);
        await using var readOnly = new NpgsqlCommand("SHOW default_transaction_read_only", source);
        Assert.Equal("on", await readOnly.ExecuteScalarAsync(token));
    }
}
