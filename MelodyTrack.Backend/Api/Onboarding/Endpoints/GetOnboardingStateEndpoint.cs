using FastEndpoints;
using MelodyTrack.Backend.Api.Onboarding.Responses;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Onboarding.Endpoints;

public class GetOnboardingStateEndpoint(OnboardingStateService stateService, ICurrentUserAccessor currentUserAccessor)
    : Ep.NoReq.Res<Results<Ok<OnboardingStateResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/onboarding");
    }

    public override async Task<Results<Ok<OnboardingStateResponse>, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var state = await stateService.GetCurrentAsync(user, ct);
        return TypedResults.Ok(OnboardingStateMapper.ToResponse(state));
    }
}
