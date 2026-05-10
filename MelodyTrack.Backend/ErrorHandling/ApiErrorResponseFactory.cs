using FastEndpoints;
using FluentValidation.Results;

namespace MelodyTrack.Backend.ErrorHandling;

public static class ApiErrorResponseFactory
{
    public static ProblemDetails CreateValidationProblemDetails(
        List<ValidationFailure> failures,
        HttpContext httpContext,
        int statusCode)
    {
        var problemDetails = new ProblemDetails(failures, httpContext.Request.Path, httpContext.TraceIdentifier, statusCode)
        {
            Detail = BuildValidationDetail(failures, statusCode)
        };

        return problemDetails;
    }

    public static ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        int statusCode,
        string? detail = null)
    {
        var problemDetails = new ProblemDetails(Array.Empty<ValidationFailure>(), httpContext.Request.Path, httpContext.TraceIdentifier, statusCode)
        {
            Detail = string.IsNullOrWhiteSpace(detail)
                ? GetDefaultDetail(statusCode)
                : detail
        };

        return problemDetails;
    }

    public static string GetTitle(int statusCode) =>
        statusCode switch
        {
            StatusCodes.Status400BadRequest => "Ошибка валидации",
            StatusCodes.Status401Unauthorized => "Требуется авторизация",
            StatusCodes.Status403Forbidden => "Доступ запрещён",
            StatusCodes.Status404NotFound => "Не найдено",
            StatusCodes.Status500InternalServerError => "Внутренняя ошибка сервера",
            _ => "Ошибка обработки запроса"
        };

    public static string GetDefaultDetail(int statusCode) =>
        statusCode switch
        {
            StatusCodes.Status400BadRequest => "Проверьте данные запроса и попробуйте снова.",
            StatusCodes.Status401Unauthorized => "Для выполнения этого запроса нужно войти в систему.",
            StatusCodes.Status403Forbidden => "У вас нет прав для выполнения этого действия.",
            StatusCodes.Status404NotFound => "Запрошенный ресурс не найден.",
            StatusCodes.Status500InternalServerError => "При обработке запроса произошла ошибка.",
            _ => "Не удалось обработать запрос."
        };

    private static string BuildValidationDetail(IReadOnlyCollection<ValidationFailure> failures, int statusCode)
    {
        var messages = failures
            .Select(f => f.ErrorMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct()
            .ToArray();

        return messages.Length switch
        {
            0 => GetDefaultDetail(statusCode),
            1 => messages[0],
            _ => "Запрос содержит несколько ошибок. Подробности перечислены в поле errors."
        };
    }
}
