using System.Security.Cryptography;
using FastEndpoints;
using MelodyTrack.Backend.Api.CalendarSubscriptions.Responses;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Dashboard;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.CalendarSubscriptions.Endpoints;

public class RegenerateUserCalendarSubscriptionEndpoint(AppDbContext db, IPublicUrlBuilder publicUrlBuilder, ICurrentUserAccessor currentUserAccessor, TimeProvider timeProvider)
    : Ep.Req<GetEntityRequest>.Res<Results<Ok<CalendarSubscriptionResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>>
{
    public override void Configure() => Post("/calendar-subscriptions/users/{id}/regenerate");

    public override async Task<Results<Ok<CalendarSubscriptionResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null) return TypedResults.Unauthorized();
        if (!currentUser.Role.RoleName.IsAnyAdmin() && currentUser.Id != req.Id) return TypedResults.Forbid();
        if (!await db.Users.AnyAsync(e => e.Id == req.Id, ct))
        {
            AddError(e => e.Id, "Пользователь не найден");
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateValidationProblemDetails(ValidationFailures, HttpContext, StatusCodes.Status404NotFound));
        }

        var active = await db.CalendarSubscriptions.Where(e => e.UserId == req.Id && e.RevokedAtUtc == null).ToListAsync(ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var subscription in active) subscription.RevokedAtUtc = nowUtc;
        var created = new CalendarSubscription { Id = Ulid.NewUlid(), UserId = req.Id, Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), CreatedAtUtc = nowUtc };
        await db.CalendarSubscriptions.AddAsync(created, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new CalendarSubscriptionResponse
        {
            Id = created.Id,
            Token = created.Token,
            Url = publicUrlBuilder.GetCalendarSubscriptionUrl(created.Token),
            FeedType = "user"
        });
    }
}
