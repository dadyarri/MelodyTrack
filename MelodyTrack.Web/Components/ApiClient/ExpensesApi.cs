using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Expenses.Requests;
using MelodyTrack.Common.Data.Models;
using MelodyTrack.Common.Utils;
using Microsoft.AspNetCore.Components;

namespace MelodyTrack.Web.Components.ApiClient;

public class ExpensesApi(ApiUtils apiUtils)
{

    public async Task<ApiResponse<CreateEntityResponse>> CreateExpenseAsync(CreateExpenseRequest request)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<CreateEntityResponse>(
            async client => await client.PostAsync("/expenses", content)
        );
    }

    public async Task<ApiResponse<object>> DeleteExpenseAsync(GetEntityRequest request)
    {
        return await apiUtils.CallApiAsync(
            async client => await client.DeleteAsync($"/expenses/{request.Id}")
        );
    }

    public async Task<ApiResponse<PaginatedResponse<Expense>>> GetExpensesAsync(GetExpensesPaginatedRequest request)
    {
        return await apiUtils.CallApiAsync<PaginatedResponse<Expense>>(
            async client => await client.GetAsync($"/expenses?{request.ToQueryString()}")
        );
    }
}