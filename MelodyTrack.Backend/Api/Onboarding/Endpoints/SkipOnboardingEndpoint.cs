using FastEndpoints;
using MelodyTrack.Backend.Api.Onboarding.Responses;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Onboarding.Endpoints;

public class SkipOnboardingEndpoint(OnboardingStateService stateService, ICurrentUserAccessor currentUserAccessor)
    : Ep.NoReq.Res<Results<Ok<OnboardingStateResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/onboarding/skip");
    }

    public override async Task<Results<Ok<OnboardingStateResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (user.Role.RoleName.IsClient())
        {
            return TypedResults.Forbid();
        }

        var state = await stateService.SkipAsync(user, ct);
        return TypedResults.Ok(OnboardingStateMapper.ToResponse(state));
    }
}
