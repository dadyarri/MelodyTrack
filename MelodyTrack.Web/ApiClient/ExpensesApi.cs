using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Expenses.Requests;
using MelodyTrack.Common.Data.Models;
using MelodyTrack.Common.Utils;

namespace MelodyTrack.Web.Components.ApiClient;

public class ExpensesApi(ApiUtils apiUtils)
{

    public async Task<ApiResponse<CreateEntityResponse>> CreateExpenseAsync(CreateExpenseRequest request)
    {
        return await apiUtils.CallApiAsync<CreateEntityResponse>(
            new HttpRequestMessage(HttpMethod.Post, "/expenses")
            {
                Content = JsonContent.Create(request)
            }
        );
    }

    public async Task<ApiResponse> DeleteExpenseAsync(GetEntityRequest request)
    {
        return await apiUtils.CallApiAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/expenses/{request.Id}")
        );
    }

    public async Task<ApiResponse<PaginatedResponse<Expense>>> GetExpensesAsync(GetExpensesPaginatedRequest request)
    {
        return await apiUtils.CallApiAsync<PaginatedResponse<Expense>>(
            new HttpRequestMessage(HttpMethod.Get, $"/expenses?{request.ToQueryString()}")
        );
    }
}