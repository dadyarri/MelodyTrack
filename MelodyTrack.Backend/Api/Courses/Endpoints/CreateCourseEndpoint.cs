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

            var themeByKey = new Dictionary<string, CourseTheme>(StringComparer.OrdinalIgnoreCase);
            var dependencyKeysByThemeId = new Dictionary<Ulid, List<string>>();

            foreach (var blockRequest in (req.Blocks ?? []).OrderBy(block => block.Order))
            {
                var block = new CourseBlock
                {
                    Id = Ulid.NewUlid(),
                    Course = course,
                    CourseId = course.Id,
                    Title = blockRequest.Title,
                    Description = blockRequest.Description,
                    Order = blockRequest.Order
                };

                foreach (var branchRequest in (blockRequest.Branches ?? []).OrderBy(branch => branch.Order))
                {
                    var branch = new CourseBranch
                    {
                        Id = Ulid.NewUlid(),
                        Block = block,
                        BlockId = block.Id,
                        Title = branchRequest.Title,
                        Description = branchRequest.Description,
                        Order = branchRequest.Order
                    };

                    foreach (var themeRequest in (branchRequest.Themes ?? []).OrderBy(theme => theme.Order))
                    {
                        var theme = new CourseTheme
                        {
                            Id = Ulid.NewUlid(),
                            Branch = branch,
                            BranchId = branch.Id,
                            Title = themeRequest.Title,
                            Description = themeRequest.Description,
                            LessonContent = themeRequest.LessonContent,
                            HomeworkContent = themeRequest.HomeworkContent,
                            Order = themeRequest.Order,
                            UnlockCostPoints = themeRequest.UnlockCostPoints,
                            EvolutionPointsReward = themeRequest.EvolutionPointsReward,
                            ExperiencePointsReward = themeRequest.ExperiencePointsReward
                        };

                        branch.Themes.Add(theme);
                        themeByKey[themeRequest.Key] = theme;
                        dependencyKeysByThemeId[theme.Id] = themeRequest.DependencyKeys
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }

                    block.Branches.Add(branch);
                }

                course.Blocks.Add(block);
            }

            foreach (var theme in course.Blocks.SelectMany(block => block.Branches).SelectMany(branch => branch.Themes))
            {
                foreach (var dependencyKey in dependencyKeysByThemeId[theme.Id])
                {
                    theme.Dependencies.Add(new CourseThemeDependency
                    {
                        Id = Ulid.NewUlid(),
                        Theme = theme,
                        ThemeId = theme.Id,
                        DependsOnTheme = themeByKey[dependencyKey],
                        DependsOnThemeId = themeByKey[dependencyKey].Id
                    });
                }
            }

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
