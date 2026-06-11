using FastEndpoints;
using MelodyTrack.Backend.Api.CourseEnrollments.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.CourseEnrollments.Endpoints;

public class UpdateCourseEnrollmentThemeProgressEndpoint(AppDbContext db, IAuditLogService auditLogService, CourseProgressService courseProgressService)
    : Ep.Req<UpdateCourseEnrollmentThemeProgressRequest>.Res<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, ProblemDetails>>
{
    public override void Configure()
    {
        Post("/course-enrollment-themes/{id}/actions");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, ProblemDetails>> ExecuteAsync(
        UpdateCourseEnrollmentThemeProgressRequest req, CancellationToken ct)
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
            .Include(item => item.Client)
            .Include(item => item.Course)
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
            .FirstOrDefaultAsync(item => item.Themes.Any(theme => theme.Id == req.Id), ct);

        var theme = enrollment?.Themes.SingleOrDefault(item => item.Id == req.Id);
        if (enrollment is null || theme is null)
        {
            AddError(item => item.Id, "Тема прогресса не найдена");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        if (!CourseEnrollmentThemeProgressActionExtensions.TryParseApiKey(req.Action, out var action))
        {
            AddError(item => item.Action, "Некорректное действие прогресса.");
            return new ProblemDetails(ValidationFailures);
        }

        var nowUtc = DateTime.UtcNow;
        courseProgressService.RefreshAvailability(enrollment, nowUtc);

        switch (action)
        {
            case CourseEnrollmentThemeProgressAction.Unlock:
                if (theme.State != CourseThemeProgressState.AvailableToUnlock)
                {
                    AddError(item => item.Action, "Эту тему сейчас нельзя открыть.");
                    return new ProblemDetails(ValidationFailures);
                }

                var availablePoints = courseProgressService.GetAvailableEvolutionPoints(enrollment);
                if (availablePoints < theme.CourseTheme.UnlockCostPoints)
                {
                    AddError(item => item.Action, "Недостаточно очков эволюции для открытия темы.");
                    return new ProblemDetails(ValidationFailures);
                }

                theme.State = CourseThemeProgressState.Unlocked;
                theme.UnlockedAtUtc ??= nowUtc;
                theme.SpentEvolutionPoints += theme.CourseTheme.UnlockCostPoints;
                enrollment.SpentEvolutionPoints += theme.CourseTheme.UnlockCostPoints;
                break;

            case CourseEnrollmentThemeProgressAction.Start:
                if (theme.State != CourseThemeProgressState.Unlocked)
                {
                    AddError(item => item.Action, "Эту тему сейчас нельзя перевести в работу.");
                    return new ProblemDetails(ValidationFailures);
                }

                theme.State = CourseThemeProgressState.InProgress;
                theme.StartedAtUtc ??= nowUtc;
                break;

            case CourseEnrollmentThemeProgressAction.SendToHomework:
                if (theme.State is not (CourseThemeProgressState.Unlocked or CourseThemeProgressState.InProgress))
                {
                    AddError(item => item.Action, "Эту тему сейчас нельзя отправить на домашнее задание.");
                    return new ProblemDetails(ValidationFailures);
                }

                theme.State = CourseThemeProgressState.WaitingForHomework;
                theme.StartedAtUtc ??= nowUtc;
                theme.WaitingForHomeworkAtUtc = nowUtc;
                break;

            case CourseEnrollmentThemeProgressAction.PassHomework:
                if (theme.State != CourseThemeProgressState.WaitingForHomework)
                {
                    AddError(item => item.Action, "Домашнее задание по этой теме еще не ожидается.");
                    return new ProblemDetails(ValidationFailures);
                }

                theme.State = CourseThemeProgressState.Completed;
                theme.CompletedAtUtc = nowUtc;
                theme.EarnedEvolutionPoints += theme.CourseTheme.EvolutionPointsReward;
                theme.EarnedExperiencePoints += theme.CourseTheme.ExperiencePointsReward;
                enrollment.EarnedEvolutionPoints += theme.CourseTheme.EvolutionPointsReward;
                enrollment.EarnedExperiencePoints += theme.CourseTheme.ExperiencePointsReward;
                courseProgressService.RefreshAvailability(enrollment, nowUtc);
                break;

            case CourseEnrollmentThemeProgressAction.ReturnToProgress:
                if (theme.State != CourseThemeProgressState.WaitingForHomework)
                {
                    AddError(item => item.Action, "Эту тему сейчас нельзя вернуть в работу.");
                    return new ProblemDetails(ValidationFailures);
                }

                theme.State = CourseThemeProgressState.InProgress;
                theme.StartedAtUtc = nowUtc;
                theme.WaitingForHomeworkAtUtc = null;
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        enrollment.UpdatedAtUtc = nowUtc;
        await db.SaveChangesAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "course_progress",
            Action = action.ToAuditAction(),
            EntityType = "course_enrollment_theme",
            EntityId = theme.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Клиент", $"{enrollment.Client.LastName} {enrollment.Client.FirstName}".Trim()),
                AuditDetailsFormatter.DescribeContext("Курс", enrollment.Course.Name),
                AuditDetailsFormatter.DescribeContext("Тема", theme.CourseTheme.Title),
                AuditDetailsFormatter.DescribeContext("Действие", action.ToDisplayName()),
                AuditDetailsFormatter.DescribeContext("Статус", theme.State.ToString()))
        }, ct);

        return TypedResults.NoContent();
    }
}
