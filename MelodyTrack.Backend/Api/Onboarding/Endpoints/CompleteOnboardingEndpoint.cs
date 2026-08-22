using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Onboarding.Responses;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Onboarding.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/onboarding/completion")]
public sealed class CompleteOnboardingEndpoint
{

    public static async Task<Results<Ok<OnboardingStateResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
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

        var state = await stateService.CompleteAsync(user, ct);
        return TypedResults.Ok(OnboardingStateMapper.ToResponse(state));
    }
}
