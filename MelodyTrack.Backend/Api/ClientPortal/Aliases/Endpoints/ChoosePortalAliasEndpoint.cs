using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.AliasDictionaries;
using MelodyTrack.Backend.Api.ClientPortal.Aliases.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ClientPortal.Aliases.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/client-portal/alias")]
public sealed class ChoosePortalAliasEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.ClientPortal)]
    public static async Task<Results<NoContent, Conflict<ApiProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        ChoosePortalAliasRequest request,
        AppDbContext db,
        PortalAliasGenerator generator,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUser.Role.RoleName.IsClient())
        {
            return TypedResults.Forbid();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users
            .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {currentUser.Id.ToByteArray()} FOR UPDATE")
            .SingleAsync(ct);
        if (user.PortalAlias is not null)
        {
            return Conflict(httpContext, "Псевдоним уже выбран и не может быть изменён.");
        }

        if (!generator.TryReadCandidate(request.CandidateToken, out var candidate)
            || candidate!.UserId != user.Id
            || candidate.EpithetId != request.EpithetId
            || candidate.NounKind != request.NounKind
            || candidate.NounId != request.NounId
            || !string.Equals(candidate.Value, request.Value, StringComparison.Ordinal))
        {
            return Conflict(httpContext, "Этот вариант имени больше недоступен. Запросите другой вариант.");
        }

        user.PortalAlias = candidate.Value;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception exception) when (AliasDictionaryErrors.IsUniqueViolation(exception))
        {
            return Conflict(httpContext, "Это имя уже выбрал другой игрок. Запросите другой вариант.");
        }

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.PortalAliasSelected,
            EntityType = "user",
            EntityId = user.Id.ToString()
        }, ct);
        await transaction.CommitAsync(ct);

        return TypedResults.NoContent();
    }

    private static Conflict<ApiProblemDetails> Conflict(HttpContext context, string detail) =>
        TypedResults.Conflict(ApiErrorResponseFactory.CreateProblemDetails(context, StatusCodes.Status409Conflict, detail));
}
