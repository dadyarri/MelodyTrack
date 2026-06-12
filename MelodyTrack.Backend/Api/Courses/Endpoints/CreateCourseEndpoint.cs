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
using Npgsql;

namespace MelodyTrack.Backend.Api.Courses.Endpoints;

public class CreateCourseEndpoint(AppDbContext db, IAuditLogService auditLogService, IRequestReplayService requestReplayService)
    : Ep.Req<CreateCourseRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    private const string ReplayEndpoint = "courses:create";

    public override void Configure()
    {
        Post("/courses");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(
        CreateCourseRequest req, CancellationToken ct)
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
                return TypedResults.Created($"/courses/{existingId}", new CreateEntityResponse
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

            var nowUtc = DateTime.UtcNow;
            var course = new Course
            {
                Id = Ulid.NewUlid(),
                Name = req.Name,
                Description = req.Description,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };

            course.Levels = req.Levels
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

            CourseStructureBuilder.PopulateCourse(course, req.Blocks);

            await db.Courses.AddAsync(course, ct);
            await db.SaveChangesAsync(ct);

            await auditLogService.WriteAsync(new AuditLogWriteRequest
            {
                Category = "courses",
                Action = "course_created",
                EntityType = "course",
                EntityId = course.Id.ToString(),
                Details = AuditDetailsFormatter.JoinChanges(
                    AuditDetailsFormatter.DescribeContext("Курс", course.Name),
                    AuditDetailsFormatter.DescribeContext("Блоков", course.Blocks.Count.ToString()),
                    AuditDetailsFormatter.DescribeContext(
                        "Тем",
                        course.Blocks.SelectMany(block => block.Branches).SelectMany(branch => branch.Themes).Count().ToString()))
            }, ct);

            if (replay is not null)
            {
                await requestReplayService.CompleteAsync(replay, course.Id, ct);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
            }

            return TypedResults.Created($"/courses/{course.Id}", new CreateEntityResponse
            {
                Id = course.Id
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
                return TypedResults.Created($"/courses/{completedId}", new CreateEntityResponse
                {
                    Id = completedId.Value
                });
            }

            throw;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }
}
