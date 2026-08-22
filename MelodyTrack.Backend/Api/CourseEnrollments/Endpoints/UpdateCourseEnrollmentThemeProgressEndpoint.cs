using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.CourseEnrollments.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.CourseEnrollments.Endpoints;

[ApiEndpoint(ApiMethod.Patch, "/course-enrollment-themes/{id}/progress")]
public sealed class UpdateCourseEnrollmentThemeProgressEndpoint
{

    public static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>, ApiProblemDetails>> HandleAsync(
        UpdateCourseEnrollmentThemeProgressRequest req,
        Ulid id,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        CourseProgressService courseProgressService,
        TimeProvider timeProvider,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        req.Id = id;
        var currentUserRole = (await currentUserAccessor.GetAsync(ct))?.Role.RoleName;
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
            validationErrors.Add(nameof(req.Id), "Тема прогресса не найдена");
            return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
        }

        if (!CourseEnrollmentThemeProgressActionExtensions.TryParseApiKey(req.Action, out var action))
        {
            validationErrors.Add(nameof(req.Action), "Некорректное действие прогресса.");
            return new ApiProblemDetails(validationErrors);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        courseProgressService.RefreshAvailability(enrollment, nowUtc);

        switch (action)
        {
            case CourseEnrollmentThemeProgressAction.Unlock:
                if (theme.State == CourseThemeProgressState.Unlocked)
                {
                    break;
                }

                if (theme.State != CourseThemeProgressState.AvailableToUnlock)
                {
                    validationErrors.Add(nameof(req.Action), "Эту тему сейчас нельзя открыть.");
                    return new ApiProblemDetails(validationErrors);
                }

                theme.State = CourseThemeProgressState.Unlocked;
                theme.UnlockedAtUtc ??= nowUtc;
                break;

            case CourseEnrollmentThemeProgressAction.Start:
                if (theme.State != CourseThemeProgressState.Unlocked)
                {
                    validationErrors.Add(nameof(req.Action), "Эту тему сейчас нельзя перевести в работу.");
                    return new ApiProblemDetails(validationErrors);
                }

                theme.State = CourseThemeProgressState.InProgress;
                theme.StartedAtUtc ??= nowUtc;
                break;

            case CourseEnrollmentThemeProgressAction.SendToHomework:
                if (theme.State is not (CourseThemeProgressState.Unlocked or CourseThemeProgressState.InProgress))
                {
                    validationErrors.Add(nameof(req.Action), "Эту тему сейчас нельзя отправить на домашнее задание.");
                    return new ApiProblemDetails(validationErrors);
                }

                theme.State = CourseThemeProgressState.WaitingForHomework;
                theme.StartedAtUtc ??= nowUtc;
                theme.WaitingForHomeworkAtUtc = nowUtc;
                break;

            case CourseEnrollmentThemeProgressAction.PassHomework:
                if (theme.State != CourseThemeProgressState.WaitingForHomework)
                {
                    validationErrors.Add(nameof(req.Action), "Домашнее задание по этой теме еще не ожидается.");
                    return new ApiProblemDetails(validationErrors);
                }

                if (!courseProgressService.IsEligibleForProgress(enrollment, theme))
                {
                    validationErrors.Add(nameof(req.Action), "Нельзя завершить тему, пока не выполнены предыдущие темы и зависимости.");
                    return new ApiProblemDetails(validationErrors);
                }

                theme.State = CourseThemeProgressState.Completed;
                theme.CompletedAtUtc = nowUtc;
                courseProgressService.RefreshAvailability(enrollment, nowUtc);
                break;

            case CourseEnrollmentThemeProgressAction.ReturnToProgress:
                if (theme.State != CourseThemeProgressState.WaitingForHomework)
                {
                    validationErrors.Add(nameof(req.Action), "Эту тему сейчас нельзя вернуть в работу.");
                    return new ApiProblemDetails(validationErrors);
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
