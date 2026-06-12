using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.CourseEnrollments.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MelodyTrack.Backend.Api.CourseEnrollments.Endpoints;

public class CreateCourseEnrollmentEndpoint(AppDbContext db, IAuditLogService auditLogService, IRequestReplayService requestReplayService)
    : Ep.Req<CreateCourseEnrollmentRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, Conflict<ProblemDetails>>>
{
    private const string ReplayEndpoint = "course-enrollments:create";

    public override void Configure()
    {
        Post("/course-enrollments");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> ExecuteAsync(
        CreateCourseEnrollmentRequest req, CancellationToken ct)
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

        var replayKey = requestReplayService.GetReplayKey(HttpContext.Request.Headers);
        if (replayKey is not null)
        {
            var existingId = await requestReplayService.TryGetResponseEntityIdAsync(ReplayEndpoint, replayKey, ct);
            if (existingId is not null)
            {
                return TypedResults.Created($"/course-enrollments/{existingId}", new CreateEntityResponse
                {
                    Id = existingId.Value
                });
            }
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        RequestReplay? replay = null;

        try
        {
            if (replayKey is not null)
            {
                transaction = await db.Database.BeginTransactionAsync(ct);
                replay = await requestReplayService.ReserveAsync(ReplayEndpoint, replayKey, ct);
            }

            var client = await db.Clients
                .FirstOrDefaultAsync(item => item.Id == req.ClientId, ct);

            if (client is null)
            {
                AddError(item => item.ClientId, "Клиент не найден");
                return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
            }

            var course = await db.Courses
                .Include(item => item.Blocks)
                    .ThenInclude(block => block.Branches)
                        .ThenInclude(branch => branch.Themes)
                            .ThenInclude(theme => theme.Dependencies)
                .FirstOrDefaultAsync(item => item.Id == req.CourseId, ct);

            if (course is null)
            {
                AddError(item => item.CourseId, "Курс не найден");
                return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
            }

            var existingEnrollment = await db.CourseEnrollments
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.ClientId == req.ClientId && item.CourseId == req.CourseId, ct);

            if (existingEnrollment is not null)
            {
                AddError(item => item.CourseId, "Клиент уже записан на этот курс.");
                return TypedResults.Conflict(new ProblemDetails(ValidationFailures));
            }

            var nowUtc = DateTime.UtcNow;
            var enrollment = new CourseEnrollment
            {
                Id = Ulid.NewUlid(),
                ClientId = req.ClientId,
                CourseId = course.Id,
                Client = client,
                Course = course,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                EarnedExperiencePoints = 0
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
                    CompletedAtUtc = null,
                    EarnedExperiencePoints = 0
                });
            }

            await db.CourseEnrollments.AddAsync(enrollment, ct);
            await db.SaveChangesAsync(ct);

            await auditLogService.WriteAsync(new AuditLogWriteRequest
            {
                Category = "course_enrollments",
                Action = "course_enrollment_created",
                EntityType = "course_enrollment",
                EntityId = enrollment.Id.ToString(),
                Details = AuditDetailsFormatter.JoinChanges(
                    AuditDetailsFormatter.DescribeContext("Клиент", $"{client.LastName} {client.FirstName}".Trim()),
                    AuditDetailsFormatter.DescribeContext("Курс", course.Name),
                    AuditDetailsFormatter.DescribeContext("Тем", enrollment.Themes.Count.ToString()))
            }, ct);

            if (replay is not null)
            {
                await requestReplayService.CompleteAsync(replay, enrollment.Id, ct);
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
        catch (DbUpdateException ex) when (replayKey is not null && IsUniqueViolation(ex))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(ct);
            }

            var completedId = await requestReplayService.WaitForResponseEntityIdAsync(ReplayEndpoint, replayKey, ct);
            if (completedId is not null)
            {
                return TypedResults.Created($"/course-enrollments/{completedId}", new CreateEntityResponse
                {
                    Id = completedId.Value
                });
            }

            throw;
        }
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

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }
}
