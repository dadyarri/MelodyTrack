using System.Security.Cryptography;
using FastEndpoints;
using MelodyTrack.Backend.Api.CalendarSubscriptions.Responses;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.CalendarSubscriptions.Endpoints;

public class RegenerateClientCalendarSubscriptionEndpoint(AppDbContext db)
    : Ep.Req<GetEntityRequest>.Res<Results<Ok<CalendarSubscriptionResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>>>
{
    public override void Configure() => Post("/calendar-subscriptions/clients/{id}/regenerate");

    public override async Task<Results<Ok<CalendarSubscriptionResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        var currentUser = await EndpointAuthUtils.GetCurrentUserContextAsync(User, db, ct);
        if (currentUser is null) return TypedResults.Unauthorized();
        if (!currentUser.Role.IsAnyAdmin() && (!currentUser.Role.IsClient() || currentUser.LinkedClientId != req.Id)) return TypedResults.Forbid();
        if (!await db.Clients.AnyAsync(e => e.Id == req.Id, ct))
        {
            AddError(e => e.Id, "Клиент не найден");
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateValidationProblemDetails(ValidationFailures, HttpContext, StatusCodes.Status404NotFound));
        }

        var active = await db.CalendarSubscriptions.Where(e => e.ClientId == req.Id && e.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var subscription in active) subscription.RevokedAtUtc = DateTime.UtcNow;
        var created = new CalendarSubscription { Id = Ulid.NewUlid(), ClientId = req.Id, Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), CreatedAtUtc = DateTime.UtcNow };
        await db.CalendarSubscriptions.AddAsync(created, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new CalendarSubscriptionResponse
        {
            Id = created.Id,
            Token = created.Token,
            Url = UserUtils.GetCalendarSubscriptionUrl(created.Token),
            FeedType = "client"
        });
    }
}
