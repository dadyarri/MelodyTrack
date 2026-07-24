using FastEndpoints;
using MelodyTrack.Backend.Api.Onboarding.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Onboarding.Endpoints;

public class ResetOnboardingEndpoint(AppDbContext db, TimeProvider timeProvider, ICurrentUserAccessor currentUserAccessor)
    : Ep.NoReq.Res<Results<Ok<OnboardingStateResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/onboarding/state/reset");
    }

    public override async Task<Results<Ok<OnboardingStateResponse>, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        await db.Entry(user).Reference(item => item.OnboardingState).LoadAsync(ct);
        var state = user.OnboardingState ?? OnboardingDefaults.CreateState(user, timeProvider);
        if (user.OnboardingState is null)
        {
            user.OnboardingState = state;
        }

        state.Status = OnboardingStatus.Active;
        state.CurrentStep = OnboardingDefaults.InitialStep;
        state.CurrentPath = OnboardingDefaults.InitialPath;
        state.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        state.CompletedAtUtc = null;

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(OnboardingStateMapper.ToResponse(state));
    }
}
