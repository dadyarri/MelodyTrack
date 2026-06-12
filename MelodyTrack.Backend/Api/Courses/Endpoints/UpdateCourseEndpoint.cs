using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Courses.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Courses.Endpoints;

public class UpdateCourseEndpoint(
    AppDbContext db,
    IAuditLogService auditLogService,
    IEntityFreshnessService entityFreshnessService,
    CourseProgressService courseProgressService)
    : Ep.Req<UpdateCourseRequest>.Res<Results<NoContent, NotFound<ProblemDetails>, ProblemDetails, UnauthorizedHttpResult, ForbidHttpResult, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Put("/courses/{id}");
    }

    public override async Task<Results<NoContent, NotFound<ProblemDetails>, ProblemDetails, UnauthorizedHttpResult, ForbidHttpResult, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(
        UpdateCourseRequest req,
        CancellationToken ct)
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

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "course",
            course.Id,
            req.ExpectedActivityId,
            "Курс был изменен другим пользователем. Обновите данные и повторите сохранение.",
            ct);

        if (conflict is not null)
        {
            return TypedResults.Conflict(conflict);
        }

        var beforeName = course.Name;
        var beforeDescription = course.Description;
        var existingBlocks = course.Blocks.ToList();
        var existingThemes = existingBlocks
            .SelectMany(block => block.Branches)
            .SelectMany(branch => branch.Themes)
            .ToList();
        var existingDependencies = existingThemes
            .SelectMany(theme => theme.Dependencies)
            .ToList();
        var existingThemeIds = existingThemes.Select(theme => theme.Id).ToList();
        var requestedThemeKeys = req.Blocks
            .SelectMany(block => block.Branches)
            .SelectMany(branch => branch.Themes)
            .Select(theme => theme.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedThemes = existingThemes
            .Where(theme => !requestedThemeKeys.Contains(theme.Key))
            .ToList();

        if (removedThemes.Count > 0)
        {
            var removedThemeIds = removedThemes.Select(theme => theme.Id).ToList();
            var hasLinkedProgress = await db.CourseEnrollmentThemes.AnyAsync(item => removedThemeIds.Contains(item.CourseThemeId), ct);
            if (hasLinkedProgress)
            {
                AddError(item => item.Id, "Нельзя удалять темы курса, которые уже участвуют в прогрессе клиентов. Измените существующую тему вместо удаления.");
                return new ProblemDetails(ValidationFailures);
            }
        }

        course.Name = req.Name;
        course.Description = req.Description;
        course.UpdatedAtUtc = DateTime.UtcNow;

        if (existingBlocks.Count > 0)
        {
            const int orderOffset = 1000;

            foreach (var block in existingBlocks)
            {
                block.Order += orderOffset;
            }

            foreach (var branch in existingBlocks.SelectMany(block => block.Branches))
            {
                branch.Order += orderOffset;
            }

            foreach (var theme in existingThemes)
            {
                theme.Order += orderOffset;
            }

            await db.SaveChangesAsync(ct);
        }

        if (existingDependencies.Count > 0)
        {
            db.CourseThemeDependencies.RemoveRange(existingDependencies);
        }

        CourseStructureBuilder.PopulateCourse(
            course,
            req.Blocks,
            existingThemes.ToDictionary(theme => theme.Key, StringComparer.OrdinalIgnoreCase));

        db.CourseBlocks.RemoveRange(existingBlocks);

        await db.SaveChangesAsync(ct);

        await SyncEnrollmentThemesAsync(course.Id, course.UpdatedAtUtc, ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "courses",
            Action = "course_updated",
            EntityType = "course",
            EntityId = course.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeChange("Название", beforeName, course.Name),
                AuditDetailsFormatter.DescribeChange("Описание", beforeDescription, course.Description),
                AuditDetailsFormatter.DescribeContext("Блоков", course.Blocks.Count.ToString()),
                AuditDetailsFormatter.DescribeContext("Тем", course.Blocks.SelectMany(block => block.Branches).SelectMany(branch => branch.Themes).Count().ToString()))
        }, ct);

        return TypedResults.NoContent();
    }

    private async Task SyncEnrollmentThemesAsync(Ulid courseId, DateTime nowUtc, CancellationToken ct)
    {
        var courseThemes = await db.CourseThemes
            .Include(item => item.Dependencies)
            .Include(item => item.Branch)
                .ThenInclude(item => item.Themes)
            .Include(item => item.Branch)
                .ThenInclude(item => item.Block)
            .Where(item => item.Branch.Block.CourseId == courseId)
            .ToListAsync(ct);

        if (courseThemes.Count == 0)
        {
            return;
        }

        var enrollments = await db.CourseEnrollments
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
                    .ThenInclude(item => item.Dependencies)
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
                    .ThenInclude(item => item.Branch)
                        .ThenInclude(item => item.Themes)
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
                    .ThenInclude(item => item.Branch)
                        .ThenInclude(item => item.Block)
            .Where(item => item.CourseId == courseId)
            .ToListAsync(ct);

        foreach (var enrollment in enrollments)
        {
            var existingThemeIds = enrollment.Themes
                .Select(item => item.CourseThemeId)
                .ToHashSet();

            foreach (var courseTheme in courseThemes)
            {
                if (existingThemeIds.Contains(courseTheme.Id))
                {
                    continue;
                }

                enrollment.Themes.Add(new CourseEnrollmentTheme
                {
                    Id = Ulid.NewUlid(),
                    Enrollment = enrollment,
                    EnrollmentId = enrollment.Id,
                    CourseTheme = courseTheme,
                    CourseThemeId = courseTheme.Id,
                    State = CourseThemeProgressState.BlockedByDependency,
                    UnlockedAtUtc = null,
                    StartedAtUtc = null,
                    WaitingForHomeworkAtUtc = null,
                    CompletedAtUtc = null,
                    SpentEvolutionPoints = 0,
                    EarnedEvolutionPoints = 0,
                    EarnedExperiencePoints = 0
                });
            }

            courseProgressService.RefreshAvailability(enrollment, nowUtc);
            enrollment.UpdatedAtUtc = nowUtc;
        }

        await db.SaveChangesAsync(ct);
    }
}
