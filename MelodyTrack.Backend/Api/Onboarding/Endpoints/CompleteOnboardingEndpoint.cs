using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Onboarding.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Onboarding.Endpoints;

public class CompleteOnboardingEndpoint(AppDbContext db)
    : Ep.NoReq.Res<Results<Ok<OnboardingStateResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/onboarding/state/complete");
    }

    public override async Task<Results<Ok<OnboardingStateResponse>, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
        if (email is null)
        {
            return TypedResults.Unauthorized();
        }

        var user = await db.Users
            .Include(e => e.OnboardingState)
            .FirstOrDefaultAsync(e => e.Email == email, ct);

        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var state = user.OnboardingState ?? OnboardingDefaults.CreateState(user);
        if (user.OnboardingState is null)
        {
            user.OnboardingState = state;
        }

        state.Status = OnboardingStatus.Completed;
        state.UpdatedAtUtc = DateTime.UtcNow;
        state.CompletedAtUtc = state.UpdatedAtUtc;

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(OnboardingStateMapper.ToResponse(state));
    }
}
