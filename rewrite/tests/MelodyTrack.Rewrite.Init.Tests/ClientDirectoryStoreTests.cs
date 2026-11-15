using System.Security.Cryptography;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Persistence;
using MelodyTrack.Rewrite.Persistence.Clients;
using MelodyTrack.Rewrite.Persistence.Security;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class ClientDirectoryStoreTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly Guid Source = Guid.Parse("019a2c63-8c00-7000-8000-000000000011");
    private static readonly Guid Kind = Guid.Parse("019a2c63-8c00-7000-8000-000000000021");
    private static readonly Guid First = Guid.Parse("019a2c63-8c00-7000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("019a2c63-8c00-7000-8000-000000000002");

    [Theory]
    [InlineData("Иванов Иван Иванович", true)]
    [InlineData("Иванов Иван Ивановичxxx", true)]
    [InlineData("Иванов Иван Ивановичxxxx", false)]
    [InlineData("  ИВАНОВ\tИВАН \n ИВАНОВИЧ  ", true)]
    [InlineData("Иван", false)]
    public async Task Read_UnifiedName_UsesDatabaseLevenshteinWithThreeEditBoundary(string query, bool matches)
    {
        await WithStore(async store =>
        {
            var page = await store.ReadAsync(new(Q: query), Token);

            Assert.Equal(matches ? 1 : 0, page.TotalCount);
            if (matches) Assert.Equal(First, Assert.Single(page.Items).Id);
            else Assert.Empty(page.Items);
        }, async database =>
        {
            await database.Query("UPDATE public.clients SET last_name='Иванов',first_name='Иван',middle_name='Иванович' WHERE id=@id")
                .WithParameter("id", First).ExecuteAsync(Token);
        });
    }

    [Fact]
    public async Task Read_UnifiedNameWithoutPatronymic_MatchesWithoutInventedTrailingField()
    {
        await WithStore(async store =>
        {
            var page = await store.ReadAsync(new(Q: "Alpha First"), Token);

            Assert.Equal(First, Assert.Single(page.Items).Id);
        });
    }

    [Fact]
    public async Task Read_NameLongerThanExtensionLimit_DoesNotTruncateOrFailContactSearch()
    {
        await WithStore(async store =>
        {
            var byName = await store.ReadAsync(new(Q: new string('A', 200)), Token);
            var byContact = await store.ReadAsync(new(Q: "79990000000"), Token);

            Assert.Empty(byName.Items);
            Assert.Equal(First, Assert.Single(byContact.Items).Id);
        }, async database =>
        {
            await database.Query("UPDATE public.clients SET last_name=@name WHERE id=@id")
                .WithParameter("name", new string('A', 260)).WithParameter("id", First).ExecuteAsync(Token);
        });
    }

    [Fact]
    public async Task Read_ProtectedPhoneSearch_FiltersBeforeTotalAndPagination()
    {
        await WithStore(async store =>
        {
            var page = await store.ReadAsync(new(Q: "+7 900 123 45 67", Archive: "all", PageSize: 1), Token);
            var beyond = await store.ReadAsync(new(Q: "79001234567", Archive: "all", Page: 2, PageSize: 1), Token);

            Assert.Equal(1, page.TotalCount);
            Assert.Equal(1, page.TotalPages);
            Assert.Equal(Second, Assert.Single(page.Items).Id);
            Assert.Equal("+7 (900) 123-45-67", Assert.Single(page.Items[0].Contacts).Value);
            Assert.Empty(beyond.Items);
            Assert.Equal(1, beyond.TotalCount);
        });
    }

    [Fact]
    public async Task Read_DefaultPagination_UsesTenRowPages()
    {
        await WithStore(async store =>
        {
            var page = await store.ReadAsync(new(), Token);

            Assert.Equal(10, page.PageSize);
            Assert.Equal(2, page.TotalCount);
        });
    }

    [Fact]
    public async Task Read_ArchiveAndSourceFilters_ReturnsOnlyMatchingProfiles()
    {
        await WithStore(async store =>
        {
            var withSource = await store.ReadAsync(new(SourceId: Source), Token);
            var withoutSource = await store.ReadAsync(new(WithoutSource: true), Token);
            var archived = await store.ReadAsync(new(Archive: "archived"), Token);

            Assert.Equal(First, Assert.Single(withSource.Items).Id);
            Assert.Equal(Second, Assert.Single(withoutSource.Items).Id);
            Assert.True(Assert.Single(archived.Items).IsArchived);
            Assert.Empty(archived.Items[0].Contacts);
        });
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task Read_NullableSort_KeepsMissingValuesLast(string direction)
    {
        await WithStore(async store =>
        {
            var page = await store.ReadAsync(new(Archive: "all", Sort: $"source:{direction}"), Token);

            Assert.Equal(First, page.Items[0].Id);
            Assert.All(page.Items.Skip(1), item => Assert.Null(item.SourceName));
        });
    }

    [Fact]
    public async Task Read_MultipleSortColumns_UsesRequestedPriorityBeforeStableIdentifier()
    {
        await WithStore(async store =>
        {
            var byCreation = await store.ReadAsync(new(Sort: "source:asc,createdAt:desc"), Token);
            var byName = await store.ReadAsync(new(Sort: "source:asc,name:asc,createdAt:desc"), Token);

            Assert.Equal([First, Second], byCreation.Items.Select(item => item.Id));
            Assert.Equal([Second, First], byName.Items.Select(item => item.Id));
        }, async database =>
        {
            await database.Query("UPDATE public.clients SET source_id=@source, last_name=CASE WHEN id=@first THEN 'Zulu' ELSE 'Alpha' END WHERE NOT is_archived")
                .WithParameter("source", Source).WithParameter("first", First).ExecuteAsync(Token);
        });
    }

    [Theory]
    [InlineData("source:asc")]
    [InlineData("")]
    public async Task Read_EqualOrAbsentSortValues_UsesIdentifierWithoutHiddenExtraSort(string sort)
    {
        await WithStore(async store =>
        {
            var page = await store.ReadAsync(new(Sort: sort), Token);

            Assert.Equal([First, Second], page.Items.Select(item => item.Id));
        }, async database =>
        {
            await database.Query("UPDATE public.clients SET source_id=@source, last_name=CASE WHEN id=@first THEN 'Zulu' ELSE 'Alpha' END WHERE NOT is_archived")
                .WithParameter("source", Source).WithParameter("first", First).ExecuteAsync(Token);
        });
    }

    [Fact]
    public async Task Read_ArchiveStatusSort_OrdersActualArchiveFlagThenRequestedName()
    {
        await WithStore(async store =>
        {
            var page = await store.ReadAsync(new(Archive: "all", Sort: "status:desc,name:desc"), Token);

            Assert.True(page.Items[0].IsArchived);
            Assert.Equal([Second, First], page.Items.Skip(1).Select(item => item.Id));
        });
    }

    [Fact]
    public async Task ReadOptions_ArchivedContactKind_RemainsAvailableForHistoricalFilters()
    {
        await WithStore(async store =>
        {
            var options = await store.ReadOptionsAsync(Token);

            Assert.Equal(Source, Assert.Single(options.Sources).Id);
            var kind = Assert.Single(options.ContactKinds);
            Assert.Equal(Kind, kind.Id);
            Assert.True(kind.IsArchived);
        });
    }

    private async Task WithStore(Func<ClientDirectoryStore, Task> scenario, Func<Database, Task>? setup = null)
    {
        var connectionString = await fixture.CreateDatabaseAsync(Token);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var database = new Database(dataSource);
        var schema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schema", "0001_CreateClients.sql"), Token);
        await database.Query(schema).ExecuteAsync(Token);
        var workState = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schema", "0014_UnifyClientWorkState.sql"), Token);
        await database.Query(workState).ExecuteAsync(Token);
        var nameSimilarity = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schema", "0015_EnableClientNameSimilarity.sql"), Token);
        await database.Query(nameSimilarity).ExecuteAsync(Token);
        using var protector = new PersonalDataProtector("test", new Dictionary<string, string>
        {
            ["test"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        });
        var protection = new ClientDataProtection(protector);
        await database.Query("""
            INSERT INTO public.client_sources(id,name) VALUES (@source,'Synthetic source');
            INSERT INTO public.contact_kinds(id,name,format,is_archived) VALUES (@kind,'Synthetic phone','phone',true);
            INSERT INTO public.clients(id,first_name,last_name,birthdate,source_id,created_at,is_archived) VALUES
                (@first,'First','Alpha','2000-01-01',@source,'2026-11-01T21:00:00Z',false),
                (@second,'Second','Beta',null,null,'2026-11-01T20:59:59Z',false),
                ('019a2c63-8c00-7000-8000-000000000003','Third','Gamma',null,null,'2026-11-03T21:00:00Z',true);
            """).WithParameter("source", Source).WithParameter("kind", Kind)
            .WithParameter("first", First).WithParameter("second", Second).ExecuteAsync(Token);
        var contact = Guid.Parse("019a2c63-8c00-7000-8000-000000000031");
        var otherContact = Guid.Parse("019a2c63-8c00-7000-8000-000000000032");
        await database.Query("""
            INSERT INTO public.client_contacts(id,client_id,kind_id,protected_value,protected_comment,is_primary)
            VALUES (@id,@owner,@kind,@value,@comment,true),(@otherId,@otherOwner,@kind,@otherValue,null,true);
            """).WithParameter("id", contact).WithParameter("owner", First).WithParameter("kind", Kind)
            .WithParameter("value", protection.ProtectValue(new(First), new(contact), "+7 999 000 00 00"))
            .WithParameter("comment", protection.ProtectComment(new(First), new(contact), "Synthetic comment"))
            .WithParameter("otherId", otherContact).WithParameter("otherOwner", Second)
            .WithParameter("otherValue", protection.ProtectValue(new(Second), new(otherContact), "+7 (900) 123-45-67"))
            .ExecuteAsync(Token);
        if (setup is not null) await setup(database);
        await scenario(new(database, protection));
    }
}
