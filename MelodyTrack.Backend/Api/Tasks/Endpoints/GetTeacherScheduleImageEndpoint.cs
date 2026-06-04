using FastEndpoints;
using MelodyTrack.Backend.Api.Tasks.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services.RecurringTasks;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Tasks.Endpoints;

public class GetTeacherScheduleImageEndpoint(AppDbContext db, ITeacherScheduleImageGenerator teacherScheduleImageGenerator)
    : Ep.Req<GetTeacherScheduleImageRequest>.Res<Results<FileContentHttpResult, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>>>
{
    private const string PngContentType = "image/png";

    public override void Configure()
    {
        Get("/tasks/teacher-schedule-image");
    }

    public override async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>>> ExecuteAsync(
        GetTeacherScheduleImageRequest req,
        CancellationToken ct)
    {
        var currentUser = await TaskAccess.GetCurrentUserAsync(User, db, ct);
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
            return TypedResults.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Detail = "Не удалось построить расписание преподавателя на выбранную дату."
            });
        }

        return TypedResults.File(image, PngContentType, $"teacher_schedule_{req.Date:yyyyMMdd}_{req.TeacherId}.png");
    }
}
