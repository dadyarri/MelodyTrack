using MelodyTrack.Backend.Api.Common.Responses;

namespace MelodyTrack.Backend.ErrorHandling;

public static class ApiErrorResponseFactory
{
    public static ApiProblemDetails CreateValidationProblemDetails(
        IReadOnlyList<ApiValidationError> failures,
        HttpContext httpContext,
        int statusCode)
    {
        var problemDetails = new ApiProblemDetails(failures, statusCode);
        ApplyRequestContext(problemDetails, httpContext);

        return problemDetails;
    }

    public static ApiProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        int statusCode,
        string? detail = null,
        string? code = null,
        string? type = null)
    {
        var problemDetails = new ApiProblemDetails
        {
            Status = statusCode,
            Type = type ?? ApiProblemTypes.ForStatus(statusCode),
            Title = GetTitle(statusCode),
            Code = code ?? ApiProblemCodes.ForStatus(statusCode),
            Detail = string.IsNullOrWhiteSpace(detail)
                ? GetDefaultDetail(statusCode)
                : detail
        };
        ApplyRequestContext(problemDetails, httpContext);

        return problemDetails;
    }

    public static StaleEntityConflictResponse CreateStaleEntityConflictProblemDetails(
        HttpContext httpContext,
        string entityType,
        Ulid entityId,
        string detail,
        RecordActivityDto? currentActivity)
    {
        var problemDetails = new StaleEntityConflictResponse
        {
            Type = ApiProblemTypes.StaleEntity,
            Title = GetTitle(StatusCodes.Status409Conflict),
            Status = StatusCodes.Status409Conflict,
            Detail = detail,
            Code = "stale_entity",
            EntityType = entityType,
            EntityId = entityId.ToString(),
            CurrentActivity = currentActivity
        };
        ApplyRequestContext(problemDetails, httpContext);
        return problemDetails;
    }

    public static void ApplyRequestContext(ApiProblemDetails problemDetails, HttpContext httpContext)
    {
        problemDetails.Instance = httpContext.Request.Path;
        problemDetails.TraceId = ApiTraceContext.GetTraceId(httpContext);
    }

    public static string GetTitle(int statusCode) =>
        statusCode switch
        {
            StatusCodes.Status400BadRequest => "Ошибка валидации",
            StatusCodes.Status401Unauthorized => "Требуется авторизация",
            StatusCodes.Status403Forbidden => "Доступ запрещён",
            StatusCodes.Status404NotFound => "Не найдено",
            StatusCodes.Status405MethodNotAllowed => "Метод не поддерживается",
            StatusCodes.Status409Conflict => "Конфликт запроса",
            StatusCodes.Status415UnsupportedMediaType => "Неподдерживаемый формат данных",
            StatusCodes.Status422UnprocessableEntity => "Операция недоступна",
            StatusCodes.Status429TooManyRequests => "Слишком много запросов",
            StatusCodes.Status500InternalServerError => "Внутренняя ошибка сервера",
            StatusCodes.Status503ServiceUnavailable => "Сервис временно недоступен",
            _ => "Ошибка обработки запроса"
        };

    public static string GetDefaultDetail(int statusCode) =>
        statusCode switch
        {
            StatusCodes.Status400BadRequest => "Проверьте данные запроса и попробуйте снова.",
            StatusCodes.Status401Unauthorized => "Для выполнения этого запроса нужно войти в систему.",
            StatusCodes.Status403Forbidden => "У вас нет прав для выполнения этого действия.",
            StatusCodes.Status404NotFound => "Запрошенный ресурс не найден.",
            StatusCodes.Status405MethodNotAllowed => "Этот метод нельзя использовать для запрошенного ресурса.",
            StatusCodes.Status409Conflict => "Запрос конфликтует с текущим состоянием ресурса.",
            StatusCodes.Status415UnsupportedMediaType => "Отправьте данные в поддерживаемом формате.",
            StatusCodes.Status422UnprocessableEntity => "Текущее состояние ресурса не позволяет выполнить операцию.",
            StatusCodes.Status429TooManyRequests => "Подождите перед повторной попыткой.",
            StatusCodes.Status500InternalServerError => "При обработке запроса произошла ошибка.",
            StatusCodes.Status503ServiceUnavailable => "Попробуйте повторить запрос позже.",
            _ => "Не удалось обработать запрос."
        };

    internal static string BuildValidationDetail(IReadOnlyCollection<ApiValidationError> failures, int statusCode)
    {
        var messages = failures
            .Select(f => f.Message)
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
