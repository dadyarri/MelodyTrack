using System.Security.Claims;
using Backend.Api.Base.Models;
using Backend.Api.Schedule.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ProblemDetails = FastEndpoints.ProblemDetails;

namespace Backend.Api.Schedule.Endpoints;

public class GetScheduleEndpoint(AppDbContext dbContext)
    : Endpoint<GetScheduleRequest, Results<Ok<PaginatedResponse<ServiceHistory>>, ForbidHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/api/schedule");
    }

    public override async Task<Results<Ok<PaginatedResponse<ServiceHistory>>, ForbidHttpResult, ProblemDetails>>
        ExecuteAsync(
            GetScheduleRequest req,
            CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (login is null) return TypedResults.Forbid();

        var user = await dbContext.Users.Where(u => u.Username == login.Value).FirstOrDefaultAsync(ct);

        if (user is null) return TypedResults.Forbid();

        var query = dbContext.Schedule
            .Include(sh => sh.Service)
            .Include(sh => sh.Client)
            .Where(sh => sh.StartDate >= req.StartDate && sh.StartDate <= req.EndDate && sh.Service.Provider == user)
            .OrderBy(sh => sh.StartDate);

        var total = await query.CountAsync(ct);
        var items = await query.ToListAsync(ct);

        return TypedResults.Ok(new PaginatedResponse<ServiceHistory>
        {
            Data = items,
            Info = new PagedInfo
            {
                Total = total,
                Page = 1,
                PageSize = total
            }
        });
    }
}