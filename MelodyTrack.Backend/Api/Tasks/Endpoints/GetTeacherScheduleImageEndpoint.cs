using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Tasks.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Services.RecurringTasks;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Tasks.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/exports/teacher-schedule")]
public sealed class GetTeacherScheduleImageEndpoint
{
    private const string PngContentType = "image/png";

        [EnableRateLimiting("expensive-read")]
    public static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>> HandleAsync(
        [AsParameters] GetTeacherScheduleImageRequest req,
        ITeacherScheduleImageGenerator teacherScheduleImageGenerator,
        ICurrentUserAccessor currentUserAccessor,
        HttpContext httpContext,
        CancellationToken ct
    )
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!TaskAccess.CanAccessTasks(currentUser))
        {
            return TypedResults.Forbid();
        }

        var image = await teacherScheduleImageGenerator.GenerateAsync(req.TeacherId, req.Date, req.Timezone, ct);
        if (image is null)
        {
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateProblemDetails(
                httpContext,
                StatusCodes.Status404NotFound,
                "Не удалось построить расписание преподавателя на выбранную дату."));
        }

        return TypedResults.File(image, PngContentType, $"teacher_schedule_{req.Date:yyyyMMdd}_{req.TeacherId}.png");
    }
}
