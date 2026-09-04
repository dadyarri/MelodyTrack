using Microsoft.AspNetCore.Authorization;

namespace MelodyTrack.Backend.Api.Auth;

public sealed class ActiveSessionEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return await next(context);
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<ActiveSessionValidator>();
        return await validator.IsActiveAsync(context.HttpContext, context.HttpContext.RequestAborted)
            ? await next(context)
            : TypedResults.Unauthorized();
    }
}
