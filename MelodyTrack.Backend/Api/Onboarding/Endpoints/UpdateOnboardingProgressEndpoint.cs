using FastEndpoints;
using MelodyTrack.Backend.Api.Onboarding.Requests;
using MelodyTrack.Backend.Api.Onboarding.Responses;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Onboarding.Endpoints;

public class UpdateOnboardingProgressEndpoint(OnboardingStateService stateService, ICurrentUserAccessor currentUserAccessor)
    : Ep.Req<UpdateOnboardingProgressRequest>.Res<Results<Ok<OnboardingStateResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Patch("/onboarding");
    }

    public override async Task<Results<Ok<OnboardingStateResponse>, UnauthorizedHttpResult>> ExecuteAsync(UpdateOnboardingProgressRequest req, CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var state = await stateService.UpdateProgressAsync(user, req.CurrentStep, req.CurrentPath, ct);
        return TypedResults.Ok(OnboardingStateMapper.ToResponse(state));
    }
}
