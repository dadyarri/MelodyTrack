using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
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

[ApiEndpoint(ApiMethod.Patch, "/courses/{id}")]
public sealed class UpdateCourseEndpoint
{

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<NoContent, NotFound<ApiProblemDetails>, ApiProblemDetails, UnauthorizedHttpResult, ForbidHttpResult, Conflict<StaleEntityConflictResponse>>> HandleAsync(
        UpdateCourseRequest req,
        Ulid id,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        IEntityFreshnessService entityFreshnessService,
        CourseProgressService courseProgressService,
        TimeProvider timeProvider,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        req.Id = id;
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
        var existingLevels = course.Levels.ToList();
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
                validationErrors.Add(nameof(req.Id), "Нельзя удалять темы курса, которые уже участвуют в прогрессе клиентов. Измените существующую тему вместо удаления.");
                return new ApiProblemDetails(validationErrors);
            }
        }

        course.Name = req.Name;
        course.Description = req.Description;
        course.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var nextLevels = req.Levels
            .OrderBy(level => level.Order)
            .Select(level => new CourseLevel
            {
                Id = Ulid.NewUlid(),
                Course = course,
                CourseId = course.Id,
                Title = level.Title,
                Order = level.Order,
                RequiredExperiencePoints = level.RequiredExperiencePoints
            })
            .ToList();

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

        if (existingLevels.Count > 0)
        {
            course.Levels.Clear();
            db.CourseLevels.RemoveRange(existingLevels);
        }

        course.Levels = nextLevels;

        CourseStructureBuilder.PopulateCourse(
            course,
            req.Blocks,
            existingThemes.ToDictionary(theme => theme.Key, StringComparer.OrdinalIgnoreCase));

        db.CourseBlocks.RemoveRange(existingBlocks);

        await db.SaveChangesAsync(ct);

        await SyncEnrollmentThemesAsync(db, courseProgressService, course.Id, course.UpdatedAtUtc, ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "courses",
            Action = "course_updated",
            EntityType = "course",
            EntityId = course.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Курс", course.Name),
                AuditDetailsFormatter.DescribeChange("Название", beforeName, course.Name),
                AuditDetailsFormatter.DescribeChange("Описание", beforeDescription, course.Description),
                AuditDetailsFormatter.DescribeContext("Блоков", course.Blocks.Count.ToString()),
                AuditDetailsFormatter.DescribeContext("Тем", course.Blocks.SelectMany(block => block.Branches).SelectMany(branch => branch.Themes).Count().ToString()))
        }, ct);

        return TypedResults.NoContent();
    }

    private static async Task SyncEnrollmentThemesAsync(
        AppDbContext db,
        CourseProgressService courseProgressService,
        Ulid courseId,
        DateTime nowUtc,
        CancellationToken ct)
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
                    CompletedAtUtc = null
                });
            }

            courseProgressService.RefreshAvailability(enrollment, nowUtc);
            enrollment.UpdatedAtUtc = nowUtc;
        }

        await db.SaveChangesAsync(ct);
    }
}
