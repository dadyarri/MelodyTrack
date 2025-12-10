namespace MelodyTrack.Common.Api.Common.Responses;

public class ApiResponse
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<ApiError> Errors { get; set; } = [];

    public static ApiResponse Success(string message = "")
    {
        return new ApiResponse
        {
            Succeeded = true,
            Message = message
        };
    }

    public static ApiResponse Failure(List<ApiError> errors, string message = "")
    {
        return new ApiResponse
        {
            Succeeded = false,
            Errors = errors,
            Message = message
        };
    }

    public new static ApiResponse Failure(string message = "")
    {
        return new ApiResponse
        {
            Succeeded = false,
            Errors = [],
            Message = message
        };
    }
}

public class ApiResponse<TData> : ApiResponse
{
    public TData Data { get; init; } = default!;

    public static ApiResponse<TData> Success(TData data, string message = "")
    {
        return new ApiResponse<TData>
        {
            Succeeded = true,
            Data = data,
            Message = message
        };
    }

    public new static ApiResponse<TData> Failure(string message = "")
    {
        return new ApiResponse<TData>
        {
            Succeeded = false,
            Errors = [],
            Message = message
        };
    }
}

// public class ApiResponse<T>
// {
//     public bool Succeeded { get; set; }
//     public T? Data { get; set; }
//     public string Message { get; set; } = string.Empty;
//     public List<ApiError> Errors { get; set; } = [];
//
//     public static ApiResponse<T> Success(T data, string message = "")
//     {
//         return new ApiResponse<T>
//         {
//             Succeeded = true,
//             Data = data,
//             Message = message
//         };
//     }
//
//     public static ApiResponse<T> Failure(string message = "")
//     {
//         return new ApiResponse<T>
//         {
//             Succeeded = false,
//             Errors = [],
//             Message = message
//         };
//     }
// }
//
// public abstract class ApiResponse : ApiResponse<object>
// {
//     public static ApiResponse<T> Success<T>(T data, string message = "")
//     {
//         return new ApiResponse<T>
//         {
//             Succeeded = true,
//             Data = data,
//             Message = message
//         };
//     }
//
//     public static ApiResponse<object> Failure(List<ApiError> errors, string message = "")
//     {
//         return new ApiResponse<object>
//         {
//             Succeeded = false,
//             Errors = errors,
//             Message = message
//         };
//     }
//
//     public new static ApiResponse<object> Failure(string message = "")
//     {
//         return new ApiResponse<object>
//         {
//             Succeeded = false,
//             Errors = [],
//             Message = message
//         };
//     }
// }