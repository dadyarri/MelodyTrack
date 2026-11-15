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
    public async Task Read_PhoneSearch_RequiresThreeNormalizedDigits()
    {
        await WithStore(async store =>
        {
            var twoDigits = await store.ReadAsync(new(Q: "+79", Archive: "all"), Token);
            var threeDigits = await store.ReadAsync(new(Q: "+790", Archive: "all"), Token);

            Assert.Empty(twoDigits.Items);
            Assert.Equal(Second, Assert.Single(threeDigits.Items).Id);
        });
    }

    [Fact]
    public async Task Read_OrdinaryPagination_ReturnsStableDisjointPagesAndEmptyResults()
    {
        await WithStore(async store =>
        {
            var first = await store.ReadAsync(new(Archive: "all", Page: 1, PageSize: 1), Token);
            var second = await store.ReadAsync(new(Archive: "all", Page: 2, PageSize: 1), Token);
            var third = await store.ReadAsync(new(Archive: "all", Page: 3, PageSize: 1), Token);
            var emptyPage = await store.ReadAsync(new(Archive: "all", Page: 4, PageSize: 1), Token);
            var noMatches = await store.ReadAsync(new(Q: "no such synthetic client", Archive: "all"), Token);

            Assert.Equal(3, first.TotalCount);
            Assert.Equal(3, first.TotalPages);
            Assert.Equal(3, second.TotalCount);
            Assert.Equal(3, third.TotalPages);
            Assert.Equal([First], first.Items.Select(item => item.Id));
            Assert.Equal([Second], second.Items.Select(item => item.Id));
            Assert.Equal([Guid.Parse("019a2c63-8c00-7000-8000-000000000003")], third.Items.Select(item => item.Id));
            Assert.Empty(emptyPage.Items);
            Assert.Equal(3, emptyPage.TotalCount);
            Assert.Empty(noMatches.Items);
            Assert.Equal(0, noMatches.TotalCount);
            Assert.Equal(0, noMatches.TotalPages);
        });
    }

    [Fact]
    public async Task Read_Contacts_MapsOwnerKindValueCommentAndPrimaryForEachContact()
    {
        var secondaryId = Guid.Parse("019a2c63-8c00-7000-8000-000000000033");

        await WithStore(async store =>
        {
            var page = await store.ReadAsync(new(Q: "+7 999 111 22 33", Archive: "all"), Token);

            var entry = Assert.Single(page.Items);
            Assert.Equal(First, entry.Id);
            var contacts = entry.Contacts.OrderBy(contact => contact.Id).ToArray();
            Assert.Collection(contacts,
                contact =>
                {
                    Assert.Equal(Guid.Parse("019a2c63-8c00-7000-8000-000000000031"), contact.Id);
                    Assert.Equal(Kind, contact.KindId);
                    Assert.Equal("Synthetic phone", contact.KindName);
                    Assert.Equal("phone", contact.Format);
                    Assert.Equal("+7 999 000 00 00", contact.Value);
                    Assert.Equal("Synthetic comment", contact.Comment);
                    Assert.True(contact.IsPrimary);
                },
                contact =>
                {
                    Assert.Equal(secondaryId, contact.Id);
                    Assert.Equal(Kind, contact.KindId);
                    Assert.Equal("Synthetic phone", contact.KindName);
                    Assert.Equal("phone", contact.Format);
                    Assert.Equal("+7 999 111 22 33", contact.Value);
                    Assert.Equal("Secondary synthetic contact", contact.Comment);
                    Assert.False(contact.IsPrimary);
                });
        }, async database =>
        {
            using var protector = new PersonalDataProtector("test", new Dictionary<string, string>
            {
                ["test"] = Convert.ToBase64String(Enumerable.Repeat((byte)29, 32).ToArray())
            });
            var protection = new ClientDataProtection(protector);
            await database.Query("""
                INSERT INTO public.client_contacts(id,client_id,kind_id,protected_value,protected_comment,is_primary)
                VALUES (@id,@owner,@kind,@value,@comment,false)
                """).WithParameter("id", secondaryId).WithParameter("owner", First).WithParameter("kind", Kind)
                .WithParameter("value", protection.ProtectValue(new(First), new(secondaryId), "+7 999 111 22 33"))
                .WithParameter("comment", protection.ProtectComment(new(First), new(secondaryId), "Secondary synthetic contact"))
                .ExecuteAsync(Token);
        });
    }

    [Fact]
    public async Task Read_EmailAndLinkSearch_MapsMatchingContactsToTheirDistinctOwners()
    {
        var firstEmailKind = Guid.Parse("019a2c63-8c00-7000-8000-000000000041");
        var secondEmailKind = Guid.Parse("019a2c63-8c00-7000-8000-000000000042");
        var linkKind = Guid.Parse("019a2c63-8c00-7000-8000-000000000043");
        var firstEmail = Guid.Parse("019a2c63-8c00-7000-8000-000000000044");
        var secondEmail = Guid.Parse("019a2c63-8c00-7000-8000-000000000045");
        var link = Guid.Parse("019a2c63-8c00-7000-8000-000000000046");

        await WithStore(async store =>
        {
            var emailPage = await store.ReadAsync(new(Q: "@synthetic.test", Archive: "all"), Token);
            var linkPage = await store.ReadAsync(new(Q: "profile.example.test", Archive: "all"), Token);

            Assert.Equal([First, Second], emailPage.Items.Select(item => item.Id));
            var firstEntry = emailPage.Items.Single(item => item.Id == First);
            var firstContact = Assert.Single(firstEntry.Contacts, contact => contact.Id == firstEmail);
            Assert.Equal("first@synthetic.test", firstContact.Value);
            var secondEntry = emailPage.Items.Single(item => item.Id == Second);
            var secondContact = Assert.Single(secondEntry.Contacts, contact => contact.Id == secondEmail);
            Assert.Equal("second@synthetic.test", secondContact.Value);
            var linkEntry = Assert.Single(linkPage.Items);
            Assert.Equal(Second, linkEntry.Id);
            var linkContact = Assert.Single(linkEntry.Contacts, contact => contact.Id == link);
            Assert.Equal(link, linkContact.Id);
            Assert.Equal("link", linkContact.Format);
            Assert.Equal("https://profile.example.test/second", linkContact.Value);
        }, async database =>
        {
            using var protector = new PersonalDataProtector("test", new Dictionary<string, string>
            {
                ["test"] = Convert.ToBase64String(Enumerable.Repeat((byte)29, 32).ToArray())
            });
            var protection = new ClientDataProtection(protector);
            await database.Query("""
                INSERT INTO public.contact_kinds(id,name,format) VALUES
                    (@firstEmailKind,'Synthetic email first','email'),
                    (@secondEmailKind,'Synthetic email second','email'),
                    (@linkKind,'Synthetic profile','link');
                INSERT INTO public.client_contacts(id,client_id,kind_id,protected_value,is_primary) VALUES
                    (@firstEmail,@first,@firstEmailKind,@firstValue,true),
                    (@secondEmail,@second,@secondEmailKind,@secondValue,true),
                    (@link,@second,@linkKind,@linkValue,true);
                """).WithParameter("firstEmailKind", firstEmailKind).WithParameter("secondEmailKind", secondEmailKind)
                .WithParameter("linkKind", linkKind).WithParameter("firstEmail", firstEmail).WithParameter("secondEmail", secondEmail)
                .WithParameter("link", link).WithParameter("first", First).WithParameter("second", Second)
                .WithParameter("firstValue", protection.ProtectValue(new(First), new(firstEmail), "first@synthetic.test"))
                .WithParameter("secondValue", protection.ProtectValue(new(Second), new(secondEmail), "second@synthetic.test"))
                .WithParameter("linkValue", protection.ProtectValue(new(Second), new(link), "https://profile.example.test/second"))
                .ExecuteAsync(Token);
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
            ["test"] = Convert.ToBase64String(Enumerable.Repeat((byte)29, 32).ToArray())
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
