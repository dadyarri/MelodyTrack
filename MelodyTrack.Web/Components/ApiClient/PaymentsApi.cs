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
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<CreateEntityResponse>(
            async client => await client.PostAsync("/payments", content)
        );
    }

    public async Task<ApiResponse<object>> DeletePaymentAsync(GetEntityRequest request)
    {
        return await apiUtils.CallApiAsync(
            async client => await client.DeleteAsync($"/payments/{request.Id}")
        );
    }

    public async Task<ApiResponse<PaginatedResponse<GetPaymentsDto>>> GetPaymentsAsync(GetPaymentsPaginatedRequest request)
    {
        return await apiUtils.CallApiAsync<PaginatedResponse<GetPaymentsDto>>(
            async client => await client.GetAsync($"/payments?{request.ToQueryString()}")
        );
    }
}