using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MelodyTrack.Rewrite.Api;
using MelodyTrack.Rewrite.Domain;
using Xunit;

namespace MelodyTrack.Rewrite.Api.Tests;

public sealed class ExpensesEndpointsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 21, 1, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 10);
    private static ExpensePostRequest Command() => new(Guid.NewGuid(), "Материалы", 123.45m, Today, null);
    private static Actor.Staff Administrator => new(StaffApiHost.Staff, StaffRole.Administrator,
        InitialPermissionPolicies.ForRole(StaffRole.Administrator));

    [Theory]
    [InlineData("POST", """{"commandId":"019a2c63-8c00-7000-8000-000000000010","description":"Материалы","date":"2026-11-10","categoryId":null}""")]
    [InlineData("POST", """{"commandId":"019a2c63-8c00-7000-8000-000000000010","description":"Материалы","amount":10,"categoryId":null}""")]
    [InlineData("PUT", """{"commandId":"019a2c63-8c00-7000-8000-000000000010","description":"Материалы","date":"2026-11-10","categoryId":null,"comment":"Исправление","expectedState":{"description":"Материалы","amount":20,"date":"2026-11-10","categoryId":null}}""")]
    [InlineData("PUT", """{"commandId":"019a2c63-8c00-7000-8000-000000000010","description":"Материалы","amount":10,"categoryId":null,"comment":"Исправление","expectedState":{"description":"Материалы","amount":20,"date":"2026-11-10","categoryId":null}}""")]
    [InlineData("PUT", """{"commandId":"019a2c63-8c00-7000-8000-000000000010","description":"Материалы","amount":10,"date":"2026-11-10","categoryId":null,"comment":"Исправление","expectedState":{"description":"Материалы","date":"2026-11-10","categoryId":null}}""")]
    [InlineData("PUT", """{"commandId":"019a2c63-8c00-7000-8000-000000000010","description":"Материалы","amount":10,"date":"2026-11-10","categoryId":null,"comment":"Исправление","expectedState":{"description":"Материалы","amount":20,"categoryId":null}}""")]
    public async Task Write_MissingRequiredExpenseField_RejectsRequest(string method, string json)
    {
        var requestType = method == "POST" ? typeof(ExpensePostRequest) : typeof(ExpensePutRequest);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, requestType, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var store = new StaffApiHost.ExpenseState();
        await using var host = await StaffApiHost.StartAsync(Administrator, Token, expenses: store, now: Now);
        using var request = new HttpRequestMessage(new(method), method == "POST" ? "/api/expenses" : $"/api/expenses/{Guid.NewGuid()}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);

        using var result = await host.Client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Post_AdministratorAfterMoscowMidnight_PreservesKopecksAndAuthor()
    {
        var store = new StaffApiHost.ExpenseState();
        await using var host = await StaffApiHost.StartAsync(Administrator, Token, expenses: store, now: Now);
        var command = Command();

        using var result = await host.SendAsync(HttpMethod.Post, "/api/expenses", command, Token);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var response = await result.Content.ReadFromJsonAsync<ExpenseResponse>(Token);
        Assert.Equal(command.CommandId, response!.Id);
        Assert.Equal(123.45m, store.Current!.Amount.Amount);
        Assert.Equal(Today, store.Current.Date);
        Assert.Equal(StaffApiHost.Staff, store.Last!.AuthorId);
    }

    [Fact]
    public async Task Post_AdministratorPreviousDay_DeniesWrite()
    {
        var store = new StaffApiHost.ExpenseState();
        await using var host = await StaffApiHost.StartAsync(Administrator, Token, expenses: store, now: Now);

        using var result = await host.SendAsync(HttpMethod.Post, "/api/expenses", Command() with { Date = Today.AddDays(-1) }, Token);

        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Put_MissingExpectedState_RejectsWithoutWriting()
    {
        var store = new StaffApiHost.ExpenseState();
        await using var host = await StaffApiHost.StartAsync(Administrator, Token, expenses: store, now: Now);

        using var result = await host.SendAsync(HttpMethod.Put, $"/api/expenses/{Guid.NewGuid()}",
            new ExpensePutRequest(Guid.NewGuid(), "Материалы", 10, Today, null, "Причина", null), Token);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Put_AdministratorMovesHistoricalExpenseToToday_DeniesWrite()
    {
        var store = new StaffApiHost.ExpenseState();
        await using var host = await StaffApiHost.StartAsync(Administrator, Token, expenses: store, now: Now);

        using var result = await host.SendAsync(HttpMethod.Put, $"/api/expenses/{Guid.NewGuid()}",
            new ExpensePutRequest(Guid.NewGuid(), "Материалы", 10, Today, null, "Причина",
                new("Материалы", 10, Today.AddDays(-1), null)), Token);

        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Put_StaleExpectedState_ReturnsConflictWithoutPrivateText()
    {
        var store = new StaffApiHost.ExpenseState();
        await using var host = await StaffApiHost.StartAsync(Administrator, Token, expenses: store, now: Now);
        var command = Command();
        using var created = await host.SendAsync(HttpMethod.Post, "/api/expenses", command, Token);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        using var result = await host.SendAsync(HttpMethod.Put, $"/api/expenses/{command.CommandId}",
            new ExpensePutRequest(Guid.NewGuid(), "private replacement", 10, Today, null, "private reason",
                new("private stale", 123.45m, Today, null)), Token);

        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.DoesNotContain("private", await result.Content.ReadAsStringAsync(Token));
        Assert.Equal(command.Description, store.Current!.Description);
    }

    [Fact]
    public async Task Put_AdministratorWithoutComment_RejectsCorrection()
    {
        var store = new StaffApiHost.ExpenseState();
        await using var host = await StaffApiHost.StartAsync(Administrator, Token, expenses: store, now: Now);

        using var result = await host.SendAsync(HttpMethod.Put, $"/api/expenses/{Guid.NewGuid()}",
            new ExpensePutRequest(Guid.NewGuid(), "Материалы", 10, Today, null, null, new("Материалы", 20, Today, null)), Token);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, store.Writes);
    }
}
