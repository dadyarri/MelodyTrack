using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Courses.Endpoints;

[ApiEndpoint(ApiMethod.Delete, "/courses/{id}")]
public sealed class DeleteCourseEndpoint
{

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<NoContent, NotFound<ApiProblemDetails>, ApiProblemDetails, UnauthorizedHttpResult, ForbidHttpResult, Conflict<StaleEntityConflictResponse>>> HandleAsync(
        [AsParameters] GetEntityRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        IEntityFreshnessService entityFreshnessService,
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
            validationErrors.Add(nameof(req.Id), "Нельзя удалить курс, который уже назначен клиентам или связан с занятиями.");
            return new ApiProblemDetails(validationErrors);
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
