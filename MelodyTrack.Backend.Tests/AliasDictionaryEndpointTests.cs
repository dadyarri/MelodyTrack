using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.AliasDictionaries;
using MelodyTrack.Backend.Api.AliasDictionaries.Requests;
using MelodyTrack.Backend.Api.AliasDictionaries.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public sealed class AliasDictionaryEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task ManageDictionary_CreateSearchEditDisableAndDelete_PersistsChanges()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        using var createdResponse = await App.Client.PostAsJsonAsync(
            "/alias-dictionaries/epithets",
            new CreateAliasDictionaryEntryRequest { Value = "  Искристый  " },
            TestContext.Current.CancellationToken);
        var created = await createdResponse.Content.ReadFromJsonAsync<AliasDictionaryEntryDto>(TestContext.Current.CancellationToken);
        using var searchResponse = await App.Client.GetAsync(
            "/alias-dictionaries/epithets?search=искр", TestContext.Current.CancellationToken);
        var search = await searchResponse.Content.ReadFromJsonAsync<GetAliasDictionaryResponse>(TestContext.Current.CancellationToken);
        using var updatedResponse = await App.Client.PatchAsJsonAsync(
            $"/alias-dictionaries/epithets/{created.ShouldNotBeNull().Id}",
            new UpdateAliasDictionaryEntryRequest { Value = "Искромётный", IsEnabled = false },
            TestContext.Current.CancellationToken);
        using var deleteResponse = await App.Client.DeleteAsync(
            $"/alias-dictionaries/epithets/{created.Id}", TestContext.Current.CancellationToken);

        createdResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Value.ShouldBe("Искристый");
        searchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        search.ShouldNotBeNull().Items.ShouldContain(item => item.Id == created.Id);
        updatedResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        db.ChangeTracker.Clear();
        (await db.AliasEpithets.FindAsync([created.Id], TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task CreateDictionaryEntry_DuplicateValue_ReturnsConflict()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));
        var value = (await db.AliasCreatures.Select(entry => entry.Value).FirstAsync(TestContext.Current.CancellationToken));

        using var response = await App.Client.PostAsJsonAsync(
            "/alias-dictionaries/creatures",
            new CreateAliasDictionaryEntryRequest { Value = value },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
