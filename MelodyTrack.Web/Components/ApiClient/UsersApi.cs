using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Users.Responses;
using Microsoft.AspNetCore.Components;

namespace MelodyTrack.Web.Components.ApiClient;

public class UsersApi(ApiUtils apiUtils)
{

    public async Task<ApiResponse<GetUsersResponse>> GetUsersAsync(NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<GetUsersResponse>(
            async client => await client.GetAsync("/users"),
            navigationManager,
            false
        );
    }
}