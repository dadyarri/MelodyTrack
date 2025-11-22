using FluentValidation.Results;

namespace MelodyTrack.Common.Api.Common.Responses;

public class ApiResponse<T>
{
    public bool Succeeded { get; set; }
    public T? Data { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<ApiError> Errors { get; set; } = [];

    public static ApiResponse<T> Success(T data, string message = "")
    {
        return new ApiResponse<T>
        {
            Succeeded = true,
            Data = data,
            Message = message
        };
    }
}

public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse<T> Success<T>(T data, string message = "")
    {
        return new ApiResponse<T>
        {
            Succeeded = true,
            Data = data,
            Message = message
        };
    }

    public static ApiResponse<object> Failure(List<ApiError> errors, string message = "")
    {
        return new ApiResponse<object>
        {
            Succeeded = false,
            Errors = errors,
            Message = message
        };
    }

    public static ApiResponse<object> Failure(string message = "")
    {
        return new ApiResponse<object>
        {
            Succeeded = false,
            Errors = [],
            Message = message
        };
    }
}