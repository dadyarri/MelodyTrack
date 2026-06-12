using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Courses.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Courses.Endpoints;

public class GetCourseEndpoint(AppDbContext db)
    : Ep.Req<GetEntityRequest>.Res<Results<Ok<GetCourseResponse>, NotFound<ProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/courses/{id}");
    }

    public override async Task<Results<Ok<GetCourseResponse>, NotFound<ProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(
        GetEntityRequest req, CancellationToken ct)
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

        var course = await db.Courses
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Blocks)
                .ThenInclude(block => block.Branches)
                    .ThenInclude(branch => branch.Themes)
                        .ThenInclude(theme => theme.Dependencies)
            .FirstOrDefaultAsync(item => item.Id == req.Id, ct);

        if (course is null)
        {
            AddError(item => item.Id, "Курс не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        return TypedResults.Ok(new GetCourseResponse
        {
            Course = CourseResponseMapper.MapCourse(course)
        });
    }
}
