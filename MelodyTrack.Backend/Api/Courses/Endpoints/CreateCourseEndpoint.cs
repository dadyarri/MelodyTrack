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

public class CreateCourseEndpoint(
    AppDbContext db,
    IAuditLogService auditLogService,
    IRequestReplayService requestReplayService,
    TimeProvider timeProvider)
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
        await using var transaction = replayKey is null ? null : await db.Database.BeginTransactionAsync(ct);
        Ulid? reservationId = null;
        if (replayKey is not null)
        {
            var decision = await requestReplayService.AcquireAsync(ReplayEndpoint, replayKey, req, ct);
            if (decision.Status == RequestReplayStatus.Completed)
            {
                return TypedResults.Created($"/courses/{decision.ResponseEntityId}", new CreateEntityResponse
                {
                    Id = decision.ResponseEntityId!.Value
                });
            }

            reservationId = decision.ReservationId;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
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

        if (reservationId is not null)
        {
            await requestReplayService.CompleteAsync(reservationId.Value, course.Id, ct);
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
}
