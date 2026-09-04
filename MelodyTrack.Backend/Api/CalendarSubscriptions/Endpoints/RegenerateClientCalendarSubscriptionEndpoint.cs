using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using System.Security.Cryptography;
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

[ApiEndpoint(ApiMethod.Post, "/clients/{id}/calendar-subscriptions")]
public sealed class RegenerateClientCalendarSubscriptionEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static async Task<Results<Ok<CalendarSubscriptionResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>> HandleAsync(
        [AsParameters] GetEntityRequest req,
        AppDbContext db,
        IPublicUrlBuilder publicUrlBuilder,
        TimeProvider timeProvider,
        ICurrentUserAccessor currentUserAccessor,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null) return TypedResults.Unauthorized();
        if (!currentUser.Role.RoleName.IsAnyAdmin() && (!currentUser.Role.RoleName.IsClient() || currentUser.ClientId != req.Id)) return TypedResults.Forbid();
        if (!await db.Clients.AnyAsync(e => e.Id == req.Id, ct))
        {
            validationErrors.Add(nameof(req.Id), "Клиент не найден");
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateValidationProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
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
