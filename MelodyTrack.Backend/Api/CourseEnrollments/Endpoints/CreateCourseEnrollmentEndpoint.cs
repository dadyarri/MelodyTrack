using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.CourseEnrollments.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.CourseEnrollments.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/course-enrollments")]
public sealed class CreateCourseEnrollmentEndpoint
{
    private const string ReplayEndpoint = "course-enrollments:create";

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>, Conflict<ApiProblemDetails>>> HandleAsync(
        CreateCourseEnrollmentRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        IRequestReplayService requestReplayService,
        TimeProvider timeProvider,
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

        var replayKey = requestReplayService.GetReplayKey(httpContext.Request.Headers);
        await using var transaction = replayKey is null ? null : await db.Database.BeginTransactionAsync(ct);
        Ulid? reservationId = null;
        if (replayKey is not null)
        {
            var decision = await requestReplayService.AcquireAsync(ReplayEndpoint, replayKey, req, ct);
            if (decision.Status == RequestReplayStatus.Completed)
            {
                return TypedResults.Created($"/course-enrollments/{decision.ResponseEntityId}", new CreateEntityResponse
                {
                    Id = decision.ResponseEntityId!.Value
                });
            }

            reservationId = decision.ReservationId;
        }

        var client = await db.Clients
            .FirstOrDefaultAsync(item => item.Id == req.ClientId, ct);

        if (client is null)
        {
            validationErrors.Add(nameof(req.ClientId), "Клиент не найден");
            return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
        }

        var course = await db.Courses
            .Include(item => item.Blocks)
                .ThenInclude(block => block.Branches)
                    .ThenInclude(branch => branch.Themes)
                        .ThenInclude(theme => theme.Dependencies)
            .FirstOrDefaultAsync(item => item.Id == req.CourseId, ct);

        if (course is null)
        {
            validationErrors.Add(nameof(req.CourseId), "Курс не найден");
            return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
        }

        var existingEnrollment = await db.CourseEnrollments
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.ClientId == req.ClientId && item.CourseId == req.CourseId, ct);

        if (existingEnrollment is not null)
        {
            validationErrors.Add(nameof(req.CourseId), "Клиент уже записан на этот курс.");
            return TypedResults.Conflict(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status409Conflict));
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var enrollment = new CourseEnrollment
        {
            Id = Ulid.NewUlid(),
            ClientId = req.ClientId,
            CourseId = course.Id,
            Client = client,
            Course = course,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        var themesById = course.Blocks
            .SelectMany(block => block.Branches.OrderBy(branch => branch.Order))
            .SelectMany(branch => branch.Themes.OrderBy(theme => theme.Order))
            .ToDictionary(theme => theme.Id);

        foreach (var theme in themesById.Values)
        {
            var state = ResolveInitialState(course, theme);
            DateTime? unlockedAtUtc = state is CourseThemeProgressState.Unlocked ? nowUtc : null;

            enrollment.Themes.Add(new CourseEnrollmentTheme
            {
                Id = Ulid.NewUlid(),
                Enrollment = enrollment,
                EnrollmentId = enrollment.Id,
                CourseTheme = theme,
                CourseThemeId = theme.Id,
                State = state,
                UnlockedAtUtc = unlockedAtUtc,
                StartedAtUtc = null,
                WaitingForHomeworkAtUtc = null,
                CompletedAtUtc = null
            });
        }

        await db.CourseEnrollments.AddAsync(enrollment, ct);
        await db.SaveChangesAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.CourseEnrollmentCreated,
            EntityType = "course_enrollment",
            EntityId = enrollment.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Клиент", $"{client.LastName} {client.FirstName}".Trim()),
                AuditDetailsFormatter.DescribeContext("Курс", course.Name),
                AuditDetailsFormatter.DescribeContext("Тем", enrollment.Themes.Count.ToString()))
        }, ct);

        if (reservationId is not null)
        {
            await requestReplayService.CompleteAsync(reservationId.Value, enrollment.Id, ct);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return TypedResults.Created($"/course-enrollments/{enrollment.Id}", new CreateEntityResponse
        {
            Id = enrollment.Id
        });
    }

    private static CourseThemeProgressState ResolveInitialState(Course course, CourseTheme theme)
    {
        if (theme.Dependencies.Count != 0)
        {
            return CourseThemeProgressState.BlockedByDependency;
        }

        var branch = course.Blocks
            .SelectMany(block => block.Branches)
            .First(item => item.Id == theme.BranchId);
        var blockOrder = course.Blocks.First(block => block.Id == branch.BlockId).Order;

        var isFirstBlock = course.Blocks.Min(block => block.Order) == blockOrder;
        var isFirstBranchTheme = branch.Themes.Min(item => item.Order) == theme.Order;

        if (!isFirstBlock || !isFirstBranchTheme)
        {
            return CourseThemeProgressState.BlockedByDependency;
        }

        return CourseThemeProgressState.Unlocked;
    }

}
