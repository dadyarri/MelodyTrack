using System.Net;
using MelodyTrack.Common.Api.Common.Responses;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace MelodyTrack.Web.Components.ApiClient;

public class ApiUtils(IHttpClientFactory factory, ProtectedLocalStorage localStorage)
{
    public async Task<ApiResponse<TResponse>> CallApiAsync<TResponse>(Func<HttpClient, Task<HttpResponseMessage>> call, bool anonymous = false)
    {
        using var client = factory.CreateClient("mt");
        var accessToken = (await localStorage.GetAsync<string>("accessToken")).Value;

        if (!anonymous && (await call(client)).StatusCode == HttpStatusCode.Unauthorized)
        {
        }

        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        var response = await call(client);
        var responseBody = await response.Content.ReadFromJsonAsync<ApiResponse<TResponse>>();
        return responseBody ?? ApiResponse<TResponse>.Failure("Ошибка разбора ответа");
    }

    public async Task<ApiResponse<object>> CallApiAsync(Func<HttpClient, Task<HttpResponseMessage>> call, bool anonymous = false)
    {
        using var client = factory.CreateClient("mt");
        var accessToken = (await localStorage.GetAsync<string>("accessToken")).Value;

        if (!anonymous && (await call(client)).StatusCode == HttpStatusCode.Unauthorized)
        {
        }

        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        var response = await call(client);
        var responseBody = await response.Content.ReadFromJsonAsync<ApiResponse>();
        return responseBody ?? ApiResponse.Failure("Ошибка разбора ответа");
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        using var client = factory.CreateClient("mt");
        var accessToken = (await localStorage.GetAsync<string>("accessToken")).Value;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        var sessionsResponse = await client.GetAsync("/auth/sessions");

        return sessionsResponse.StatusCode != HttpStatusCode.Unauthorized;
    }
}