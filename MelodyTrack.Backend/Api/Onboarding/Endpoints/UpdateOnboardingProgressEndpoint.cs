using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Onboarding.Requests;
using MelodyTrack.Backend.Api.Onboarding.Responses;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Onboarding.Endpoints;

[ApiEndpoint(ApiMethod.Patch, "/onboarding")]
public sealed class UpdateOnboardingProgressEndpoint
{

    public static async Task<Results<Ok<OnboardingStateResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        UpdateOnboardingProgressRequest req,
        OnboardingStateService stateService,
        ICurrentUserAccessor currentUserAccessor,
        CancellationToken ct
    )
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

        var state = await stateService.UpdateProgressAsync(user, req.CurrentStep, req.CurrentPath, ct);
        return TypedResults.Ok(OnboardingStateMapper.ToResponse(state));
    }
}
