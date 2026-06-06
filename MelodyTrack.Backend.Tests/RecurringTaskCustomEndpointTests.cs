using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Tasks.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class RecurringTaskCustomEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task CreateCustomTask_ForExternalRecipient_CreatesOpenTask()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var dueAtUtc = DateTime.UtcNow.AddHours(2);
        var response = await App.Client.PostAsJsonAsync(
            "/tasks/custom",
            new
            {
                recipientName = "Новый контакт",
                phone = "+79990001122",
                title = "Позвонить и предложить занятие",
                messageText = "Добрый день! Напоминаю о нашем разговоре.",
                dueAtUtc
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var payload = await response.Content.ReadFromJsonAsync<CreateEntityResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();

        var dueResponse = await App.Client.GetAsync("/tasks/due?timezone=Europe/Moscow&type=custom-task&status=open", TestContext.Current.CancellationToken);
        dueResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var duePayload = await dueResponse.Content.ReadFromJsonAsync<GetDueRecurringTasksResponse>(cancellationToken: TestContext.Current.CancellationToken);

        duePayload.ShouldNotBeNull();
        duePayload.Tasks.ShouldContain(task => task.RuleId == payload.Id && task.RelatedPersonDisplayName == "Новый контакт");
    }
}
