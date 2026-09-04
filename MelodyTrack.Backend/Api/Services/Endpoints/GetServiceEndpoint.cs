using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Services.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/services/{id}")]
public sealed class GetServiceEndpoint
{

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<Ok<ServiceWithCurrentPriceDto>, NotFound<ApiProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        [AsParameters] GetEntityRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IRecordActivityService recordActivityService,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        var currentUserRole = (await currentUserAccessor.GetAsync(ct))?.Role.RoleName;
        if (currentUserRole is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUserRole.Value.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var service = await db.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == req.Id, ct);

        if (service is null)
        {
            validationErrors.Add(nameof(req.Id), "Сервис не найден");
            return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
        }

        var latestPrice = await db.ServicePriceHistory
            .AsNoTracking()
            .Where(item => item.Service.Id == service.Id)
            .OrderByDescending(item => item.EffectiveDate)
            .Select(item => (decimal?)item.Price)
            .FirstOrDefaultAsync(ct);

        return TypedResults.Ok(new ServiceWithCurrentPriceDto
        {
            Id = service.Id,
            Name = service.Name,
            Description = service.Description,
            Price = latestPrice ?? 0m,
            LastActivity = await recordActivityService.GetLatestActivityAsync("service", service.Id.ToString(), ct)
        });
    }
}
