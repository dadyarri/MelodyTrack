using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Schedule.Requests;
using MelodyTrack.Common.Api.Schedule.Responses;
using MelodyTrack.Common.Utils;
using Microsoft.AspNetCore.Components;

namespace MelodyTrack.Web.Components.ApiClient;

public class ScheduleApi(ApiUtils apiUtils)
{

    public async Task<(CreateEntityResponse?, HttpResponseMessage)> CreateAppointmentAsync(CreateAppointmentRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<CreateEntityResponse>(
            client => client.PostAsync("/appointments", content),
            navigationManager,
            anonymous: false
        );
    }
    
    public async Task<HttpResponseMessage> DeleteAppointmentAsync(GetEntityRequest request, NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync(
            async client => await client.DeleteAsync($"/appointments/{request.Id}"),
            navigationManager,
            anonymous: false
        );
    }

    public async Task<(GetAppointmentsResponse?, HttpResponseMessage)> GetAppointmentsAsync(GetAppointmentsRequest request, NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<GetAppointmentsResponse>(
            async client => await client.GetAsync($"/appointments?{request.ToQueryString()}"),
            navigationManager,
            anonymous: false
        );
    }

    public async Task<(GetMiniScheduleResponse?, HttpResponseMessage)> GetMiniScheduleAsync(BaseGetAppointmentsRequest request, NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<GetMiniScheduleResponse>(
            async client => await client.GetAsync($"/appointments/mini?{request.ToQueryString()}"),
            navigationManager,
            anonymous: false
        );
    }
    
    public async Task<(GetEntityRequest?, HttpResponseMessage)> UpdateAppointmentAsync(UpdateAppointmentRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);
        return await apiUtils.CallApiAsync<GetEntityRequest>(
            async client => await client.PutAsync($"/appointments/{request.Id}", content),
            navigationManager,
            anonymous: false
        );
    }
}