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

    public async Task<ApiResponse<CreateEntityResponse>> CreateClientAsync(CreateClientRequest request)
    {
        return await apiUtils.CallApiAsync<CreateEntityResponse>(
            new HttpRequestMessage(HttpMethod.Post, "/clients") { Content = JsonContent.Create(request) }
        );
    }

    public async Task<ApiResponse> DeleteClientAsync(GetEntityRequest request)
    {
        return await apiUtils.CallApiAsync(new HttpRequestMessage(HttpMethod.Delete, $"/clients/{request.Id}"));
    }

    public async Task<ApiResponse<Client>> GetClientAsync(GetEntityRequest request)
    {
        return await apiUtils.CallApiAsync<Client>(new HttpRequestMessage(HttpMethod.Get, $"/clients/{request.Id}"));
    }

    public async Task<ApiResponse<PaginatedResponse<ClientWithBalanceDto>>> GetClientsAsync(GetClientsPaginatedRequest request)
    {
        return await apiUtils.CallApiAsync<PaginatedResponse<ClientWithBalanceDto>>(new HttpRequestMessage(HttpMethod.Get, $"/clients?{request.ToQueryString()}")
        );
    }

    public async Task<ApiResponse<GetClientsWithNegativeBalanceResponse>> GetClientsWithNegativeBalanceAsync()
    {
        return await apiUtils.CallApiAsync<GetClientsWithNegativeBalanceResponse>(new HttpRequestMessage(HttpMethod.Get, "/clients/inDebt")
        );
    }

    public async Task<ApiResponse<LookupClientsResponse>> LookupClientsAsync()
    {
        return await apiUtils.CallApiAsync<LookupClientsResponse>(new HttpRequestMessage(HttpMethod.Get, "/clients/lookup"));
    }

    public async Task<ApiResponse<GetEntityRequest>> UpdateClientAsync(UpdateClientRequest request)
    {
        return await apiUtils.CallApiAsync<GetEntityRequest>(
            new HttpRequestMessage(HttpMethod.Put, $"/clients/{request.Id}")
            {
                Content = JsonContent.Create(request)
            }
        );
    }
}