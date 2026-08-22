using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Courses.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Courses.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/courses/{id}")]
public sealed class GetCourseEndpoint
{

    public static async Task<Results<Ok<GetCourseResponse>, NotFound<ApiProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        [AsParameters] GetEntityRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
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

        var course = await db.Courses
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Levels)
            .Include(item => item.Blocks)
                .ThenInclude(block => block.Branches)
                    .ThenInclude(branch => branch.Themes)
                        .ThenInclude(theme => theme.Dependencies)
            .FirstOrDefaultAsync(item => item.Id == req.Id, ct);

        if (course is null)
        {
            validationErrors.Add(nameof(req.Id), "Курс не найден");
            return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
        }

        return TypedResults.Ok(new GetCourseResponse
        {
            Course = CourseResponseMapper.MapCourse(course)
        });
    }
}
