using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace MelodyTrack.Backend.Api.Auth.PreProcessors;

public sealed class ActiveSessionPreProcessor : GlobalPreProcessor<ActiveSessionPreProcessor.State>
{
    public sealed class State;

    public override async Task PreProcessAsync(IPreProcessorContext context, State state, CancellationToken ct)
    {
        var validator = context.HttpContext.RequestServices.GetRequiredService<ActiveSessionValidator>();
        if (await validator.IsActiveAsync(context.HttpContext, ct))
        {
            return;
        }

        await context.HttpContext.Response.SendUnauthorizedAsync(ct);
    }
}
