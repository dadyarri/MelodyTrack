using FluentValidation.Results;
using Microsoft.AspNetCore.Http;

namespace MelodyTrack.Common.Api.Common.Responses;

public static class ApiResults
{
    public static IResult Ok<T>(T data, string message = "")
    {
        var response = ApiResponse<T>.Success(data, message);
        return TypedResults.Json(response, statusCode: StatusCodes.Status200OK);
    }

    public static IResult Ok(string message = "Успех")
    {
        var response = ApiResponse.Success(message);
        return TypedResults.Json(response, statusCode: StatusCodes.Status200OK);
    }

    public static IResult Created<T>(string uri, T data, string message = "Ресурс успешно создан")
    {
        var response = ApiResponse<T>.Success(data, message);
        return TypedResults.Created(uri, response);
    }

    public static IResult NoContent()
    {
        return TypedResults.NoContent();
    }

    public static IResult NotFound(List<ValidationFailure>? failures = null, string message = "Запрошенный ресурс не был найден")
    {
        List<ApiError> errors = [];
        if (failures is not null)
        {
            errors = failures.ToApiErrors();
        }
        var response = ApiResponse.Failure(errors, message);
        return TypedResults.Json(response, statusCode: StatusCodes.Status404NotFound);
    }

    public static IResult Unauthorized(string message = "Авторизация провалилась")
    {
        var response = ApiResponse.Failure(message);
        return TypedResults.Json(response, statusCode: StatusCodes.Status401Unauthorized);
    }


    public static IResult Forbid(string message = "У вас нет доступа к этому ресурсу", string errorCode = "FORBIDDEN")
    {
        var error = new ApiError
        {
            Code = errorCode,
            Message = message
        };
        var response = ApiResponse.Failure([error], message);
        return TypedResults.Json(response, statusCode: StatusCodes.Status403Forbidden);
    }
}