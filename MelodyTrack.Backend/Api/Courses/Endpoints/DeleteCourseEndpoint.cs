using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Courses.Endpoints;

public class DeleteCourseEndpoint(AppDbContext db, IAuditLogService auditLogService, IEntityFreshnessService entityFreshnessService)
    : Ep.Req<GetEntityRequest>.Res<Results<NoContent, NotFound<ProblemDetails>, ProblemDetails, UnauthorizedHttpResult, ForbidHttpResult, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Delete("/courses/{id}");
    }

    public override async Task<Results<NoContent, NotFound<ProblemDetails>, ProblemDetails, UnauthorizedHttpResult, ForbidHttpResult, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(
        GetEntityRequest req,
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
            .AsNoTracking()
            .Where(item => item.Id == req.Id)
            .Select(item => new { item.Id, item.Name, item.Description })
            .FirstOrDefaultAsync(ct);

        if (course is null)
        {
            return TypedResults.NoContent();
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "course",
            course.Id,
            req.ExpectedActivityId,
            "Курс был изменен другим пользователем. Проверьте последние изменения перед удалением.",
            ct);

        if (conflict is not null)
        {
            return TypedResults.Conflict(conflict);
        }

        var hasEnrollments = await db.CourseEnrollments.AnyAsync(item => item.CourseId == req.Id, ct);
        var hasLinkedAppointments = await db.Appointments.AnyAsync(item => item.CourseThemeId != null && item.CourseTheme!.Branch.Block.CourseId == req.Id, ct);

        if (hasEnrollments || hasLinkedAppointments)
        {
            AddError(item => item.Id, "Нельзя удалить курс, который уже назначен клиентам или связан с занятиями.");
            return new ProblemDetails(ValidationFailures);
        }

        var themeIds = await db.CourseThemes
            .Where(item => item.Branch.Block.CourseId == req.Id)
            .Select(item => item.Id)
            .ToListAsync(ct);

        if (themeIds.Count > 0)
        {
            await db.CourseThemeDependencies
                .Where(item => themeIds.Contains(item.ThemeId) || themeIds.Contains(item.DependsOnThemeId))
                .ExecuteDeleteAsync(ct);
        }

        await db.Courses.Where(item => item.Id == req.Id).ExecuteDeleteAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "courses",
            Action = "course_deleted",
            EntityType = "course",
            EntityId = course.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Курс", course.Name),
                AuditDetailsFormatter.DescribeContext("Описание", course.Description))
        }, ct);

        return TypedResults.NoContent();
    }
}
