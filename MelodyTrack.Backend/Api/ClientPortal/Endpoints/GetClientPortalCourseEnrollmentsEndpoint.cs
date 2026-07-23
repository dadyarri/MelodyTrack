using FastEndpoints;
using MelodyTrack.Backend.Api.CourseEnrollments.Responses;
using MelodyTrack.Backend.Api.Courses.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ClientPortal.Endpoints;

public class GetClientPortalCourseEnrollmentsEndpoint(AppDbContext db, CourseProgressService courseProgressService)
    : Ep.NoReq.Res<Results<Ok<GetCourseEnrollmentsResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/client-portal/course-enrollments");
    }

    public override async Task<Results<Ok<GetCourseEnrollmentsResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var currentUser = await EndpointAuthUtils.GetCurrentUserContextAsync(User, db, ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUser.Role.IsClient() || currentUser.LinkedClientId is null)
        {
            return TypedResults.Forbid();
        }

        var enrollments = await db.CourseEnrollments
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Client)
            .Include(item => item.Course)
                .ThenInclude(item => item.Levels)
            .Include(item => item.Course)
                .ThenInclude(course => course.Blocks)
                    .ThenInclude(block => block.Branches)
                        .ThenInclude(branch => branch.Themes)
                            .ThenInclude(theme => theme.Dependencies)
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
            .Where(item => item.ClientId == currentUser.LinkedClientId.Value)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync(ct);

        var linkedAppointments = await LoadLinkedAppointmentsAsync(currentUser.LinkedClientId.Value, enrollments, ct);

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
                Course = CourseResponseMapper.MapCourse(enrollment.Course),
                CurrentLevel = courseProgressService.ResolveCurrentLevel(enrollment) is { } level
                    ? new CourseEnrollmentLevelDto
                    {
                        Id = level.Id,
                        Title = level.Title,
                        Order = level.Order,
                        RequiredExperiencePoints = level.RequiredExperiencePoints
                    }
                    : null,
                EarnedExperiencePoints = courseProgressService.CalculateEarnedExperiencePoints(enrollment),
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
                        EarnedExperiencePoints = courseProgressService.CalculateEarnedExperiencePoints(theme),
                        RecentAppointments = linkedAppointments
                            .GetValueOrDefault(theme.CourseThemeId, [])
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

    private async Task<Dictionary<Ulid, List<LinkedAppointmentRow>>> LoadLinkedAppointmentsAsync(
        Ulid clientId,
        List<Data.Models.CourseEnrollment> enrollments,
        CancellationToken ct)
    {
        var themeIds = enrollments
            .SelectMany(item => item.Themes)
            .Select(item => item.CourseThemeId)
            .Distinct()
            .ToList();

        if (themeIds.Count == 0)
        {
            return [];
        }

        var linkedAppointments = await db.Appointments
            .AsNoTracking()
            .Where(item =>
                !item.IsDeleted &&
                item.CourseThemeId != null &&
                item.Client.Id == clientId &&
                themeIds.Contains(item.CourseThemeId.Value))
            .Include(item => item.Provider)
            .OrderByDescending(item => item.StartDate)
            .Select(item => new LinkedAppointmentRow
            {
                Id = item.Id,
                CourseThemeId = item.CourseThemeId!.Value,
                StartDate = item.StartDate,
                Status = item.Status,
                ProviderDisplayName = item.Provider == null ? null : item.Provider.FirstName + " " + item.Provider.LastName,
                LessonNotes = item.LessonNotes
            })
            .ToListAsync(ct);

        return linkedAppointments
            .GroupBy(item => item.CourseThemeId)
            .ToDictionary(group => group.Key, group => group.Take(5).ToList());
    }

    private sealed class LinkedAppointmentRow
    {
        public required Ulid Id { get; set; }
        public required Ulid CourseThemeId { get; set; }
        public required DateTime StartDate { get; set; }
        public required AppointmentStatus Status { get; set; }
        public string? ProviderDisplayName { get; set; }
        public string? LessonNotes { get; set; }
    }
}
