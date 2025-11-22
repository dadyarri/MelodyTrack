using System.Text;
using FluentValidation.Results;
using MelodyTrack.Common.Api.Common.Responses;

namespace MelodyTrack.Common.Api;

public static class ValidationFailureExtensions
{
    private static ApiError ToApiError(this ValidationFailure failure)
    {
        var message = new StringBuilder($"Ошибка валидации свойства {failure.PropertyName}: {failure.ErrorMessage}");

        if (failure.AttemptedValue is not null)
        {
            message.Append($" (полученное значение: {failure.AttemptedValue})");
        }

        return new ApiError
        {
            Code = "VALIDATION_FAILURE",
            Message = message.ToString(),
            Field = failure.PropertyName
        };
    }

    public static List<ApiError> ToApiErrors(this IEnumerable<ValidationFailure> failures)
    {
        return failures.Select(e => e.ToApiError()).ToList();
    }
}