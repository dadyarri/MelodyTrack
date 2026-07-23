using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.CourseEnrollments.Endpoints;

public class DeleteCourseEnrollmentEndpoint(AppDbContext db, IAuditLogService auditLogService)
    : Ep.Req<GetEntityRequest>.Res<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Delete("/course-enrollments/{id}");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(
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

        var enrollment = await db.CourseEnrollments
            .AsNoTracking()
            .Where(item => item.Id == req.Id)
            .Select(item => new
            {
                item.Id,
                item.ClientId,
                ClientDisplayName = (item.Client.LastName + " " + item.Client.FirstName).Trim(),
                item.CourseId,
                CourseName = item.Course.Name
            })
            .FirstOrDefaultAsync(ct);

        if (enrollment is null)
        {
            return TypedResults.NoContent();
        }

        await db.CourseEnrollments
            .Where(item => item.Id == req.Id)
            .ExecuteDeleteAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "course_enrollments",
            Action = "course_enrollment_deleted",
            EntityType = "course_enrollment",
            EntityId = enrollment.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Клиент", enrollment.ClientDisplayName),
                AuditDetailsFormatter.DescribeContext("Курс", enrollment.CourseName))
        }, ct);

        return TypedResults.NoContent();
    }
}
