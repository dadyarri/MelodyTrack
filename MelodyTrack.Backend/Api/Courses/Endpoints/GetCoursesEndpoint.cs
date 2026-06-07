using FastEndpoints;
using MelodyTrack.Backend.Api.Courses.Requests;
using MelodyTrack.Backend.Api.Courses.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Courses.Endpoints;

public class GetCoursesEndpoint(AppDbContext db)
    : Ep.Req<GetCoursesRequest>.Res<Results<Ok<GetCoursesResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/courses");
    }

    public override async Task<Results<Ok<GetCoursesResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(
        GetCoursesRequest req, CancellationToken ct)
    {
        var currentUserRole = await EndpointAuthUtils.GetCurrentUserRoleAsync(User, db, ct);
        if (currentUserRole is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUserRole.Value.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var query = db.Courses
            .AsNoTracking()
            .Include(course => course.Blocks)
                .ThenInclude(block => block.Branches)
                    .ThenInclude(branch => branch.Themes)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var search = req.Search.Trim();
            query = query.Where(course =>
                EF.Functions.ILike(course.Name, $"%{search}%")
                || (course.Description != null && EF.Functions.ILike(course.Description, $"%{search}%")));
        }

        var courses = await query
            .OrderBy(course => course.Name)
            .ToListAsync(ct);

        return TypedResults.Ok(new GetCoursesResponse
        {
            Courses = courses
                .Select(course => new CourseSummaryDto
                {
                    Id = course.Id,
                    Name = course.Name,
                    Description = course.Description,
                    BlockCount = course.Blocks.Count,
                    ThemeCount = course.Blocks.SelectMany(block => block.Branches).SelectMany(branch => branch.Themes).Count(),
                    UpdatedAtUtc = course.UpdatedAtUtc
                })
                .ToList()
        });
    }
}
