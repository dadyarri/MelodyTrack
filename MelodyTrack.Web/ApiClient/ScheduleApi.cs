using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Schedule.Requests;
using MelodyTrack.Common.Api.Schedule.Responses;
using MelodyTrack.Common.Utils;
using Microsoft.AspNetCore.Components;

namespace MelodyTrack.Web.Components.ApiClient;

public class ScheduleApi(ApiUtils apiUtils)
{

    public async Task<ApiResponse<CreateEntityResponse>> CreateAppointmentAsync(CreateAppointmentRequest request)
    {
        return await apiUtils.CallApiAsync<CreateEntityResponse>(
            new HttpRequestMessage(HttpMethod.Post, "/appointments")
            {
                Content = JsonContent.Create(request)
            }
        );
    }

    public async Task<ApiResponse> DeleteAppointmentAsync(GetEntityRequest request)
    {
        return await apiUtils.CallApiAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/appointments/{request.Id}")
        );
    }

    public async Task<ApiResponse<GetAppointmentsResponse>> GetAppointmentsAsync(GetAppointmentsRequest request)
    {
        return await apiUtils.CallApiAsync<GetAppointmentsResponse>(
            new HttpRequestMessage(HttpMethod.Get, $"/appointments?{request.ToQueryString()}")
        );
    }

    public async Task<ApiResponse<GetMiniScheduleResponse>> GetMiniScheduleAsync(BaseGetAppointmentsRequest request)
    {
        return await apiUtils.CallApiAsync<GetMiniScheduleResponse>(
            new HttpRequestMessage(HttpMethod.Get, $"/appointments/mini?{request.ToQueryString()}")
        );
    }

    public async Task<ApiResponse<GetEntityRequest>> UpdateAppointmentAsync(UpdateAppointmentRequest request)
    {
        return await apiUtils.CallApiAsync<GetEntityRequest>(
            new HttpRequestMessage(HttpMethod.Put, $"/appointments/{request.Id}")
            {
                Content = JsonContent.Create(request)
            }
        );
    }
}