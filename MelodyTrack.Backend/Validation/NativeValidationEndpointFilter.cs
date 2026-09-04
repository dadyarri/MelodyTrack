using System.ComponentModel.DataAnnotations;
using MelodyTrack.Backend.ErrorHandling;

namespace MelodyTrack.Backend.Validation;

public sealed class NativeValidationEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var failures = new List<ApiValidationError>();
        foreach (var request in context.Arguments.OfType<IValidatableRequest>())
        {
            var validationContext = new ValidationContext(request, context.HttpContext.RequestServices, null);
            foreach (var result in ((IValidatableObject)request).Validate(validationContext))
            {
                var paths = result.MemberNames.DefaultIfEmpty(string.Empty);
                failures.AddRange(paths.Select(path => new ApiValidationError
                {
                    Path = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(path),
                    Code = "validation_error",
                    Message = result.ErrorMessage ?? "Некорректное значение"
                }));
            }
        }

        return failures.Count == 0
            ? await next(context)
            : ApiErrorResponseFactory.CreateValidationProblemDetails(
                failures,
                context.HttpContext,
                StatusCodes.Status400BadRequest);
    }
}
