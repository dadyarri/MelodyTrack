using MelodyTrack.Common.Api.Clients.Requests;
using MelodyTrack.Common.Api.Clients.Responses;
using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data.Models;
using MelodyTrack.Common.Utils;
using Microsoft.AspNetCore.Components;

namespace MelodyTrack.Web.Components.ApiClient;

public class ClientsApi(ApiUtils apiUtils)
{

    public async Task<ApiResponse<CreateEntityResponse>> CreateClientAsync(CreateClientRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<CreateEntityResponse>(
            async client => await client.PostAsync("/clients", content),
            navigationManager,
            false
        );
    }

    public async Task<ApiResponse<object>> DeleteClientAsync(GetEntityRequest request, NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync(
            async client => await client.DeleteAsync($"/clients/{request.Id}"),
            navigationManager,
            false
        );
    }

    public async Task<ApiResponse<Client>> GetClientAsync(GetEntityRequest request, NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<Client>(
            async client => await client.GetAsync($"/clients/{request.Id}"),
            navigationManager,
            false
        );
    }

    public async Task<ApiResponse<PaginatedResponse<ClientWithBalanceDto>>> GetClientsAsync(GetClientsPaginatedRequest request, NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<PaginatedResponse<ClientWithBalanceDto>>(
            async client => await client.GetAsync($"/clients?{request.ToQueryString()}"),
            navigationManager,
            false
        );
    }

    public async Task<ApiResponse<GetClientsWithNegativeBalanceResponse>> GetClientsWithNegativeBalanceAsync(NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<GetClientsWithNegativeBalanceResponse>(
            async client => await client.GetAsync("/clients/inDebt"),
            navigationManager,
            false
        );
    }

    public async Task<ApiResponse<LookupClientsResponse>> LookupClientsAsync(NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<LookupClientsResponse>(
            async client => await client.GetAsync("/clients/lookup"),
            navigationManager,
            false
        );
    }

    public async Task<ApiResponse<GetEntityRequest>> UpdateClientAsync(UpdateClientRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);
        return await apiUtils.CallApiAsync<GetEntityRequest>(
            async client => await client.PutAsync($"/clients/{request.Id}", content),
            navigationManager,
            false
        );
    }
}