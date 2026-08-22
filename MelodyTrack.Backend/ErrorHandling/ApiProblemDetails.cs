using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelodyTrack.Backend.ErrorHandling;

public class ApiProblemDetails : IResult
{
    public string Type { get; set; } = ApiProblemTypes.Validation;
    public string Title { get; set; } = ApiErrorResponseFactory.GetTitle(StatusCodes.Status400BadRequest);
    public int Status { get; set; } = StatusCodes.Status400BadRequest;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; set; }

    public string Instance { get; set; } = string.Empty;
    public string Code { get; set; } = ApiProblemCodes.Validation;
    public string TraceId { get; set; } = string.Empty;

    public IReadOnlyList<ApiValidationError> Errors { get; set; } = [];

    public ApiProblemDetails()
    {
    }

    public ApiProblemDetails(IReadOnlyList<ApiValidationError> failures, int statusCode = StatusCodes.Status400BadRequest)
    {
        Status = statusCode;
        Type = ApiProblemTypes.ForStatus(statusCode, hasValidationErrors: true);
        Title = ApiErrorResponseFactory.GetTitle(statusCode);
        Code = ApiProblemCodes.ForStatus(statusCode, hasValidationErrors: true);
        Detail = ApiErrorResponseFactory.BuildValidationDetail(failures, statusCode);
        Errors = failures.DistinctBy(error => new { error.Path, error.Code, error.Message }).ToArray();
    }

    public ApiProblemDetails(IReadOnlyList<ApiValidationError> failures, HttpContext httpContext, int statusCode)
        : this(failures, statusCode)
    {
        ApiErrorResponseFactory.ApplyRequestContext(this, httpContext);
    }

    public Task ExecuteAsync(HttpContext httpContext)
    {
        ApiErrorResponseFactory.ApplyRequestContext(this, httpContext);
        httpContext.Response.StatusCode = Status;
        httpContext.Response.ContentType = ApiMediaTypes.ProblemJson;
        return httpContext.Response.WriteAsJsonAsync(
            this,
            GetType(),
            JsonSerializerOptions.Web,
            ApiMediaTypes.ProblemJson,
            httpContext.RequestAborted);
    }
}

public sealed class ApiValidationErrorCollection : List<ApiValidationError>
{
    public void Add(string path, string message, string code = "validation_error") =>
        Add(new ApiValidationError
        {
            Path = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(path),
            Code = code,
            Message = message
        });
}

public sealed class ApiValidationError
{
    public required string Path { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }

    [JsonIgnore]
    public string Name => Path;

    [JsonIgnore]
    public string Reason => Message;
}

public static class ApiMediaTypes
{
    public const string ProblemJson = "application/problem+json";
}

public static class ApiTraceContext
{
    public static string GetTraceId(HttpContext context) =>
        Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
}

public static class ApiProblemCodes
{
    public const string MalformedRequest = "malformed_request";
    public const string Validation = "validation_failed";
    public const string Unauthorized = "authentication_required";
    public const string Forbidden = "access_denied";
    public const string NotFound = "resource_not_found";
    public const string Conflict = "resource_conflict";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string UnprocessableEntity = "invalid_transition";
    public const string RateLimited = "rate_limited";
    public const string ServiceUnavailable = "service_unavailable";
    public const string InternalError = "internal_error";

    public static string ForStatus(int statusCode, bool hasValidationErrors = false) =>
        statusCode switch
        {
            StatusCodes.Status400BadRequest when hasValidationErrors => Validation,
            StatusCodes.Status400BadRequest => MalformedRequest,
            StatusCodes.Status401Unauthorized => Unauthorized,
            StatusCodes.Status403Forbidden => Forbidden,
            StatusCodes.Status404NotFound => NotFound,
            StatusCodes.Status409Conflict => Conflict,
            StatusCodes.Status422UnprocessableEntity => UnprocessableEntity,
            StatusCodes.Status429TooManyRequests => RateLimited,
            StatusCodes.Status503ServiceUnavailable => ServiceUnavailable,
            StatusCodes.Status500InternalServerError => InternalError,
            _ => $"http_{statusCode}"
        };
}

public static class ApiProblemTypes
{
    public const string MalformedRequest = "urn:melody-track:problem:malformed-request";
    public const string Validation = "urn:melody-track:problem:validation";
    public const string Unauthorized = "urn:melody-track:problem:authentication-required";
    public const string Forbidden = "urn:melody-track:problem:access-denied";
    public const string NotFound = "urn:melody-track:problem:resource-not-found";
    public const string Conflict = "urn:melody-track:problem:resource-conflict";
    public const string StaleEntity = "urn:melody-track:problem:stale-entity";
    public const string IdempotencyConflict = "urn:melody-track:problem:idempotency-conflict";
    public const string UnprocessableEntity = "urn:melody-track:problem:invalid-transition";
    public const string RateLimited = "urn:melody-track:problem:rate-limited";
    public const string ServiceUnavailable = "urn:melody-track:problem:service-unavailable";
    public const string InternalError = "urn:melody-track:problem:internal-error";

    public static string ForStatus(int statusCode, bool hasValidationErrors = false) =>
        statusCode switch
        {
            StatusCodes.Status400BadRequest when hasValidationErrors => Validation,
            StatusCodes.Status400BadRequest => MalformedRequest,
            StatusCodes.Status401Unauthorized => Unauthorized,
            StatusCodes.Status403Forbidden => Forbidden,
            StatusCodes.Status404NotFound => NotFound,
            StatusCodes.Status409Conflict => Conflict,
            StatusCodes.Status422UnprocessableEntity => UnprocessableEntity,
            StatusCodes.Status429TooManyRequests => RateLimited,
            StatusCodes.Status503ServiceUnavailable => ServiceUnavailable,
            StatusCodes.Status500InternalServerError => InternalError,
            _ => "about:blank"
        };
}
