using System.Net;
using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Common.Api.Auth.Responses;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace MelodyTrack.Web.Components.ApiClient;

public class ApiUtils(IHttpClientFactory factory, ProtectedLocalStorage localStorage)
{
    public async Task<(TResponse?, HttpResponseMessage)> CallApiAsync<TResponse>(Func<HttpClient, Task<HttpResponseMessage>> call, NavigationManager nav, bool anonymous = false)
    {
        using var client = factory.CreateClient("mt");
        var accessToken = (await localStorage.GetAsync<string>("accessToken")).Value;

        if (!anonymous && (await call(client)).StatusCode == HttpStatusCode.Unauthorized)
        {
            await RefreshAccessTokenAsync(client, nav);
        }

        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        var response = await call(client);
        return (await response.Content.ReadFromJsonAsync<TResponse>(), response);
    }
    public async Task<HttpResponseMessage> CallApiAsync(Func<HttpClient, Task<HttpResponseMessage>> call, NavigationManager nav, bool anonymous = false)
    {
        using var client = factory.CreateClient("mt");
        var accessToken = (await localStorage.GetAsync<string>("accessToken")).Value;

        if (!anonymous && (await call(client)).StatusCode == HttpStatusCode.Unauthorized)
        {
            await RefreshAccessTokenAsync(client, nav);
        }

        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        var response = await call(client);
        return response;
    }

    private async Task RefreshAccessTokenAsync(HttpClient client, NavigationManager nav)
    {
        var accessToken = (await localStorage.GetAsync<string>("accessToken")).Value;
        var refreshToken = (await localStorage.GetAsync<string>("refreshToken")).Value;

        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        var sessionsResponse = await client.GetAsync("/auth/sessions");

        if (sessionsResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            if (refreshToken is null)
            {
                nav.NavigateTo("/login");
                return;
            }

            client.DefaultRequestHeaders.Remove("Authorization");
            var request = JsonContent.Create(new RefreshRequest
            {
                RefreshToken = refreshToken!
            });

            var refreshResponse = await client.PostAsync("/auth/refresh", request);

            if (refreshResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                nav.NavigateTo("/login");
                return;
            }

            var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<LoginResponse>();
            await localStorage.SetAsync("accessToken", refreshBody!.AccessToken);
            await localStorage.SetAsync("refreshToken", refreshBody.RefreshToken);
        }

    }
}