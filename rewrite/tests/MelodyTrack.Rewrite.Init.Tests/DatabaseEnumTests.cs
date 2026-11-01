using System.Security.Cryptography;
using System.Text;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Init;
using MelodyTrack.Rewrite.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class DatabaseEnumTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    public sealed class ContactKindRow
    {
        public Guid Id { get; set; }
        public ContactFormat Format { get; set; }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task WithDatabase(Func<Database, Task> scenario)
    {
        var connectionString = await fixture.CreateDatabaseAsync(Token);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(Token);
            await MigrationRunner.PrepareAsync(connection, Token);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Schema", "0001_CreateClients.sql"), Token);
            var migration = new Migration(1, "0001_CreateClients.sql", Convert.ToHexString(SHA256.HashData(bytes)),
                context => SqlMigration.RunAsync(Encoding.UTF8.GetString(bytes), context));
            await MigrationRunner.ApplyAsync(connection, migration, NullLogger.Instance, Token);
        }

        // Enum types exist before the mapped data source opens its first connection.
        await using var source = new NpgsqlDataSourceBuilder(connectionString).MapDomainEnums().Build();
        await scenario(new Database(source));
    }

    [Theory]
    [InlineData(ContactFormat.Phone, "phone")]
    [InlineData(ContactFormat.Email, "email")]
    [InlineData(ContactFormat.Link, "link")]
    public Task WriteAndRead_ContactFormat_RoundTripsThroughDapper(ContactFormat format, string label) => WithDatabase(async database =>
    {
        var id = Guid.Parse("019a2c63-8c00-7000-8000-000000000101");

        var written = await database.Query("INSERT INTO public.contact_kinds (id, name, format) VALUES (@id, 'Contact', @format) RETURNING format")
            .WithParameter("id", id)
            .WithParameter("format", format)
            .GetSingleAsync<ContactFormat>(Token);

        Assert.Equal(format, written);
        var storedLabel = await database.Query("SELECT format::text FROM public.contact_kinds WHERE id = @id")
            .WithParameter("id", id).GetSingleAsync<string>(Token);
        Assert.Equal(label, storedLabel);
        var row = await database.Query("SELECT id, format FROM public.contact_kinds WHERE id = @id")
            .WithParameter("id", id).GetSingleAsync<ContactKindRow>(Token);
        Assert.Equal(id, row.Id);
        Assert.Equal(format, row.Format);
    });

    [Fact]
    public Task Read_NullContactFormat_ReturnsNull() => WithDatabase(async database =>
    {
        var value = await database.Query("SELECT CAST(@format AS public.contact_format)")
            .WithParameter("format", null)
            .GetSingleAsync<ContactFormat?>(Token);

        Assert.Null(value);
    });

    [Fact]
    public Task Insert_UnknownEnumLabel_RejectsValue() => WithDatabase(async database =>
    {
        var operation = database.Query("INSERT INTO public.contact_kinds (id, name, format) VALUES (@id, 'Contact', CAST(@format AS public.contact_format))")
            .WithParameter("id", Guid.Parse("019a2c63-8c00-7000-8000-000000000101"))
            .WithParameter("format", "unknown");

        var error = await Assert.ThrowsAsync<PostgresException>(() => operation.ExecuteAsync(Token));

        Assert.Equal(PostgresErrorCodes.InvalidTextRepresentation, error.SqlState);
    });

    [Fact]
    public Task WriteAndRead_TransactionalQuery_PreservesEnumBinding() => WithDatabase(async database =>
    {
        await using var transaction = await database.BeginTransactionAsync(Token);
        var id = Guid.Parse("019a2c63-8c00-7000-8000-000000000101");

        await transaction.Query("INSERT INTO public.contact_kinds (id, name, format) VALUES (@id, 'Contact', @format)")
            .WithParameter("id", id)
            .WithParameter("format", ContactFormat.Email)
            .ExecuteAsync(Token);
        await transaction.CommitAsync(Token);

        var format = await database.Query("SELECT format FROM public.contact_kinds WHERE id = @id")
            .WithParameter("id", id).GetSingleAsync<ContactFormat>(Token);
        Assert.Equal(ContactFormat.Email, format);
    });
}
