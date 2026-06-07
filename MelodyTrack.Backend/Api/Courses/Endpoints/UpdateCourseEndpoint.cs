using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Courses.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Courses.Endpoints;

public class UpdateCourseEndpoint(AppDbContext db, IAuditLogService auditLogService, IEntityFreshnessService entityFreshnessService)
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

        var hasEnrollments = await db.CourseEnrollments.AnyAsync(item => item.CourseId == course.Id, ct);
        if (hasEnrollments)
        {
            AddError(item => item.Id, "Нельзя изменить шаблон курса, который уже назначен клиентам.");
            return new ProblemDetails(ValidationFailures);
        }

        var hasLinkedAppointments = await db.Appointments.AnyAsync(item => item.CourseThemeId != null && item.CourseTheme!.Branch.Block.CourseId == course.Id, ct);
        if (hasLinkedAppointments)
        {
            AddError(item => item.Id, "Нельзя изменить шаблон курса, который уже связан с проведенными или запланированными занятиями.");
            return new ProblemDetails(ValidationFailures);
        }

        var beforeName = course.Name;
        var beforeDescription = course.Description;

        course.Name = req.Name;
        course.Description = req.Description;
        course.UpdatedAtUtc = DateTime.UtcNow;

        if (course.Blocks.Count > 0)
        {
            var existingThemeIds = course.Blocks
                .SelectMany(block => block.Branches)
                .SelectMany(branch => branch.Themes)
                .Select(theme => theme.Id)
                .ToList();

            if (existingThemeIds.Count > 0)
            {
                await db.CourseThemeDependencies
                    .Where(item => existingThemeIds.Contains(item.ThemeId) || existingThemeIds.Contains(item.DependsOnThemeId))
                    .ExecuteDeleteAsync(ct);
            }

            db.CourseBlocks.RemoveRange(course.Blocks);
            course.Blocks.Clear();
            await db.SaveChangesAsync(ct);
        }

        CourseStructureBuilder.PopulateCourse(course, req.Blocks);

        await db.SaveChangesAsync(ct);
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
}
