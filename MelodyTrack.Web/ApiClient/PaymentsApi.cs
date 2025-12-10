using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Payments.Requests;
using MelodyTrack.Common.Api.Payments.Responses;
using MelodyTrack.Common.Utils;
using Microsoft.AspNetCore.Components;

namespace MelodyTrack.Web.Components.ApiClient;

public class PaymentsApi(ApiUtils apiUtils)
{
    public async Task<ApiResponse<CreateEntityResponse>> CreatePaymentAsync(CreatePaymentRequest request)
    {

        return await apiUtils.CallApiAsync<CreateEntityResponse>(
            new HttpRequestMessage(HttpMethod.Post, "/payments")
            {
                Content = JsonContent.Create(request)
            }
        );
    }

    public async Task<ApiResponse> DeletePaymentAsync(GetEntityRequest request)
    {
        return await apiUtils.CallApiAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/payments/{request.Id}")
        );
    }

    public async Task<ApiResponse<PaginatedResponse<GetPaymentsDto>>> GetPaymentsAsync(GetPaymentsPaginatedRequest request)
    {
        return await apiUtils.CallApiAsync<PaginatedResponse<GetPaymentsDto>>(
            new HttpRequestMessage(HttpMethod.Get, $"/payments?{request.ToQueryString()}")
        );
    }
}