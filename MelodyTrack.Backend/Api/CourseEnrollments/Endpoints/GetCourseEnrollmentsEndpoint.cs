using FastEndpoints;
using MelodyTrack.Backend.Api.CourseEnrollments.Requests;
using MelodyTrack.Backend.Api.CourseEnrollments.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.CourseEnrollments.Endpoints;

public class GetCourseEnrollmentsEndpoint(AppDbContext db, CourseProgressService courseProgressService)
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
            .AsSplitQuery()
            .Include(item => item.Client)
            .Include(item => item.Course)
                .ThenInclude(course => course.Levels)
            .Include(item => item.Themes)
                .ThenInclude(theme => theme.CourseTheme)
            .AsQueryable();

        if (req.ClientId is not null)
        {
            query = query.Where(item => item.ClientId == req.ClientId.Value);
        }

        if (req.CourseId is not null)
        {
            query = query.Where(item => item.CourseId == req.CourseId.Value);
        }

        var enrollments = await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync(ct);

        var linkedAppointments = await LoadLinkedAppointmentsAsync(enrollments, ct);

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
                CurrentLevel = courseProgressService.ResolveCurrentLevel(enrollment) is { } level
                    ? new CourseEnrollmentLevelDto
                    {
                        Id = level.Id,
                        Title = level.Title,
                        Order = level.Order,
                        RequiredExperiencePoints = level.RequiredExperiencePoints
                    }
                    : null,
                EarnedExperiencePoints = enrollment.EarnedExperiencePoints,
                Themes = enrollment.Themes
                    .OrderBy(theme => theme.CourseTheme.Title)
                    .Select(theme => new CourseEnrollmentThemeDto
                    {
                        Id = theme.Id,
                        CourseThemeId = theme.CourseThemeId,
                        ThemeTitle = theme.CourseTheme.Title,
                        ThemeDescription = theme.CourseTheme.Description,
                        LessonContent = theme.CourseTheme.LessonContent,
                        HomeworkContent = theme.CourseTheme.HomeworkContent,
                        ExperiencePointsReward = theme.CourseTheme.ExperiencePointsReward,
                        State = theme.State,
                        UnlockedAtUtc = theme.UnlockedAtUtc,
                        StartedAtUtc = theme.StartedAtUtc,
                        WaitingForHomeworkAtUtc = theme.WaitingForHomeworkAtUtc,
                        CompletedAtUtc = theme.CompletedAtUtc,
                        EarnedExperiencePoints = theme.EarnedExperiencePoints,
                        RecentAppointments = linkedAppointments
                            .GetValueOrDefault((enrollment.ClientId, theme.CourseThemeId), [])
                            .Select(item => new CourseEnrollmentThemeAppointmentDto
                            {
                                Id = item.Id,
                                StartDateUtc = item.StartDate,
                                ProviderDisplayName = item.ProviderDisplayName,
                                Status = item.Status.ToApiKey(),
                                LessonNotes = item.LessonNotes
                            })
                            .ToList()
                    })
                    .ToList()
            }).ToList()
        });
    }

    private async Task<Dictionary<(Ulid ClientId, Ulid CourseThemeId), List<LinkedAppointmentRow>>> LoadLinkedAppointmentsAsync(
        List<Data.Models.CourseEnrollment> enrollments,
        CancellationToken ct)
    {
        var clientIds = enrollments.Select(item => item.ClientId).Distinct().ToList();
        var themeIds = enrollments
            .SelectMany(item => item.Themes)
            .Select(item => item.CourseThemeId)
            .Distinct()
            .ToList();

        if (clientIds.Count == 0 || themeIds.Count == 0)
        {
            return [];
        }

        var linkedAppointments = await db.Appointments
            .AsNoTracking()
            .Where(item =>
                !item.IsDeleted &&
                item.CourseThemeId != null &&
                clientIds.Contains(item.Client.Id) &&
                themeIds.Contains(item.CourseThemeId.Value))
            .Include(item => item.Provider)
            .OrderByDescending(item => item.StartDate)
            .Select(item => new LinkedAppointmentRow
            {
                Id = item.Id,
                ClientId = item.Client.Id,
                CourseThemeId = item.CourseThemeId!.Value,
                StartDate = item.StartDate,
                Status = item.Status,
                ProviderDisplayName = item.Provider == null ? null : item.Provider.FirstName + " " + item.Provider.LastName,
                LessonNotes = item.LessonNotes
            })
            .ToListAsync(ct);

        return linkedAppointments
            .GroupBy(item => (item.ClientId, item.CourseThemeId))
            .ToDictionary(
                group => group.Key,
                group => group.Take(5).ToList());
    }

    private sealed class LinkedAppointmentRow
    {
        public required Ulid Id { get; set; }
        public required Ulid ClientId { get; set; }
        public required Ulid CourseThemeId { get; set; }
        public required DateTime StartDate { get; set; }
        public required AppointmentStatus Status { get; set; }
        public string? ProviderDisplayName { get; set; }
        public string? LessonNotes { get; set; }
    }
}
