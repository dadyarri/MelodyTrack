using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Services.Requests;
using MelodyTrack.Common.Api.Services.Responses;
using MelodyTrack.Common.Utils;
using Microsoft.AspNetCore.Components;

namespace MelodyTrack.Web.Components.ApiClient;

public class ServicesApi(ApiUtils apiUtils)
{

    public async Task<(CreateEntityResponse?, HttpResponseMessage)> CreateServiceAsync(CreateServiceRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<CreateEntityResponse>(
            async client => await client.PostAsync("/services", content),
            navigationManager,
            anonymous: false
        );
    }

    public async Task<(PaginatedResponse<ServiceWithCurrentPriceDto>?, HttpResponseMessage)> GetServicesAsync(GetServicesPaginatedRequest request, NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<PaginatedResponse<ServiceWithCurrentPriceDto>>(
            async client => await client.GetAsync($"/services?{request.ToQueryString()}"),
            navigationManager,
            anonymous: false
        );
    }

    public async Task<(LookupServicesResponse?, HttpResponseMessage)> LookupServicesAsync(NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<LookupServicesResponse>(
            async client => await client.GetAsync("/services/lookup"),
            navigationManager,
            anonymous: false
        );
    }

    public async Task<HttpResponseMessage> DeleteServiceAsync(GetEntityRequest request, NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync(
            async client => await client.DeleteAsync($"/services/{request.Id}"),
            navigationManager,
            anonymous: false
        );
    }
}