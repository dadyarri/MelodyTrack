using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Services.Requests;
using MelodyTrack.Common.Api.Services.Responses;
using MelodyTrack.Common.Utils;
using Microsoft.AspNetCore.Components;

namespace MelodyTrack.Web.Components.ApiClient;

public class ServicesApi(ApiUtils apiUtils)
{

    public async Task<ApiResponse<CreateEntityResponse>> CreateServiceAsync(CreateServiceRequest request)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<CreateEntityResponse>(
            new HttpRequestMessage(HttpMethod.Post, "/services")
            {
                Content = content
            }
        );
    }

    public async Task<ApiResponse<PaginatedResponse<ServiceWithCurrentPriceDto>>> GetServicesAsync(GetServicesPaginatedRequest request)
    {
        return await apiUtils.CallApiAsync<PaginatedResponse<ServiceWithCurrentPriceDto>>(
            new HttpRequestMessage(HttpMethod.Get, $"/services?{request.ToQueryString()}")
        );
    }

    public async Task<ApiResponse<LookupServicesResponse>> LookupServicesAsync(NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<LookupServicesResponse>(
            new HttpRequestMessage(HttpMethod.Get, "/services/lookup")
        );
    }

    public async Task<ApiResponse> DeleteServiceAsync(GetEntityRequest request)
    {
        return await apiUtils.CallApiAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/services/{request.Id}")
        );
    }
}