using System.Security.Cryptography;
using FastEndpoints;
using MelodyTrack.Backend.Api.CalendarSubscriptions.Responses;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.CalendarSubscriptions.Endpoints;

public class RegenerateClientCalendarSubscriptionEndpoint(
    AppDbContext db,
    IPublicUrlBuilder publicUrlBuilder,
    TimeProvider timeProvider,
    ICurrentUserAccessor currentUserAccessor)
    : Ep.Req<GetEntityRequest>.Res<Results<Ok<CalendarSubscriptionResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>>
{
    public override void Configure() => Post("/clients/{id}/calendar-subscriptions");

    public override async Task<Results<Ok<CalendarSubscriptionResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null) return TypedResults.Unauthorized();
        if (!currentUser.Role.RoleName.IsAnyAdmin() && (!currentUser.Role.RoleName.IsClient() || currentUser.ClientId != req.Id)) return TypedResults.Forbid();
        if (!await db.Clients.AnyAsync(e => e.Id == req.Id, ct))
        {
            AddError(e => e.Id, "Клиент не найден");
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateValidationProblemDetails(ValidationFailures, HttpContext, StatusCodes.Status404NotFound));
        }

        var active = await db.CalendarSubscriptions.Where(e => e.ClientId == req.Id && e.RevokedAtUtc == null).ToListAsync(ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var subscription in active) subscription.RevokedAtUtc = nowUtc;
        var created = new CalendarSubscription { Id = Ulid.NewUlid(), ClientId = req.Id, Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), CreatedAtUtc = nowUtc };
        await db.CalendarSubscriptions.AddAsync(created, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new CalendarSubscriptionResponse
        {
            Id = created.Id,
            Token = created.Token,
            Url = publicUrlBuilder.GetCalendarSubscriptionUrl(created.Token),
            FeedType = "client"
        });
    }
}
