using FastEndpoints;
using MelodyTrack.Backend.Api.CourseEnrollments.Requests;
using MelodyTrack.Backend.Api.CourseEnrollments.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.CourseEnrollments.Endpoints;

public class GetCourseEnrollmentsEndpoint(AppDbContext db)
    : Ep.Req<GetCourseEnrollmentsRequest>.Res<Results<Ok<GetCourseEnrollmentsResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/course-enrollments");
    }

    public override async Task<Results<Ok<GetCourseEnrollmentsResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(
        GetCourseEnrollmentsRequest req, CancellationToken ct)
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

        var query = db.CourseEnrollments
            .AsNoTracking()
            .Include(item => item.Client)
            .Include(item => item.Course)
            .Include(item => item.Themes)
                .ThenInclude(theme => theme.CourseTheme)
            .AsQueryable();

        if (req.ClientId is not null)
        {
            query = query.Where(item => item.ClientId == req.ClientId.Value);
        }

        var enrollments = await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync(ct);

        return TypedResults.Ok(new GetCourseEnrollmentsResponse
        {
            Enrollments = enrollments.Select(enrollment => new CourseEnrollmentDto
            {
                Id = enrollment.Id,
                ClientId = enrollment.ClientId,
                ClientDisplayName = $"{enrollment.Client.LastName} {enrollment.Client.FirstName}".Trim(),
                CourseId = enrollment.CourseId,
                CourseName = enrollment.Course.Name,
                CreatedAtUtc = enrollment.CreatedAtUtc,
                EarnedEvolutionPoints = enrollment.EarnedEvolutionPoints,
                SpentEvolutionPoints = enrollment.SpentEvolutionPoints,
                EarnedExperiencePoints = enrollment.EarnedExperiencePoints,
                Themes = enrollment.Themes
                    .OrderBy(theme => theme.CourseTheme.Title)
                    .Select(theme => new CourseEnrollmentThemeDto
                    {
                        Id = theme.Id,
                        CourseThemeId = theme.CourseThemeId,
                        ThemeTitle = theme.CourseTheme.Title,
                        State = theme.State,
                        UnlockedAtUtc = theme.UnlockedAtUtc,
                        StartedAtUtc = theme.StartedAtUtc,
                        WaitingForHomeworkAtUtc = theme.WaitingForHomeworkAtUtc,
                        CompletedAtUtc = theme.CompletedAtUtc,
                        SpentEvolutionPoints = theme.SpentEvolutionPoints,
                        EarnedEvolutionPoints = theme.EarnedEvolutionPoints,
                        EarnedExperiencePoints = theme.EarnedExperiencePoints
                    })
                    .ToList()
            }).ToList()
        });
    }
}
