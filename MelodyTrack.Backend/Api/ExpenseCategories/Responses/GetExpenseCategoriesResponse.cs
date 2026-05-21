using MelodyTrack.Backend.Api.Common.Responses;

namespace MelodyTrack.Backend.Api.ExpenseCategories.Responses;

public class GetExpenseCategoriesResponse
{
    public required List<ReferenceBookItemDto> Categories { get; set; }
}
