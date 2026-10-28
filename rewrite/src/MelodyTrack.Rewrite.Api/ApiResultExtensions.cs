using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Rewrite.Api;

public sealed record ApiIssue(string Code, string Message, string? Field);

public static class ApiResultExtensions
{
    public static Results<NoContent, ProblemHttpResult> ToHttpResult(this UnitResult<OperationError> result,
        Func<DomainIssue, ApiIssue> mapIssue, string? traceId = null)
    {
        var value = result.IsSuccess ? Result.Success<bool, OperationError>(true) : Result.Failure<bool, OperationError>(result.Error);
        return value.ToHttpResult(_ => TypedResults.NoContent(), mapIssue, traceId);
    }

    public static Results<TSuccess, ProblemHttpResult> ToHttpResult<T, TSuccess>(this Result<T, OperationError> result,
        Func<T, TSuccess> success, Func<DomainIssue, ApiIssue> mapIssue, string? traceId = null)
        where TSuccess : Microsoft.AspNetCore.Http.IResult
    {
        if (result.IsSuccess) return success(result.Value);

        var (status, title, errors) = result.Error switch
        {
            OperationError.Validation validation => (400, "Ошибка проверки данных", validation.Errors),
            OperationError.Conflict conflict => (409, "Операция конфликтует с текущим состоянием", conflict.Errors),
            OperationError.NotFound => (404, "Объект не найден", (ErrorList?)null),
            OperationError.Forbidden => (403, "Доступ запрещён", (ErrorList?)null)
        };
        var problem = new ProblemDetails { Status = status, Title = title };
        if (errors is not null) problem.Extensions["errors"] = errors.Issues.Select(mapIssue).ToArray();
        if (traceId is not null) problem.Extensions["traceId"] = traceId;
        return TypedResults.Problem(problem);
    }
}
