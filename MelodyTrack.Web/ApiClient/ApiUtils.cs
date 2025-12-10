using System.Net;
using System.Net.Http.Headers;
using MelodyTrack.Common.Api.Common.Responses;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace MelodyTrack.Web.Components.ApiClient;

public class ApiUtils(IHttpClientFactory factory, ProtectedLocalStorage localStorage)
{
    public async Task<ApiResponse<TResponse>> CallApiAsync<TResponse>(HttpRequestMessage requestMessage, bool anonymous = false)
    {
        using var client = factory.CreateClient("mt");

        if (!anonymous)
        {
            var accessToken = (await localStorage.GetAsync<string>("accessToken")).Value;
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.SendAsync(requestMessage);
        var responseBody = await response.Content.ReadFromJsonAsync<ApiResponse<TResponse>>();
        return responseBody ?? ApiResponse<TResponse>.Failure("Ошибка разбора ответа");
    }

    public async Task<ApiResponse> CallApiAsync(HttpRequestMessage requestMessage, bool anonymous = false)
    {
        using var client = factory.CreateClient("mt");
        if (!anonymous)
        {
            var accessToken = (await localStorage.GetAsync<string>("accessToken")).Value;
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.SendAsync(requestMessage);
        var responseBody = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
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