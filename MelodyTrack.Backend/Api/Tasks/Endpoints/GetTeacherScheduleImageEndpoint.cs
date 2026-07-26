using FastEndpoints;
using MelodyTrack.Backend.Api.Tasks.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Services.RecurringTasks;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Tasks.Endpoints;

public class GetTeacherScheduleImageEndpoint(ITeacherScheduleImageGenerator teacherScheduleImageGenerator, ICurrentUserAccessor currentUserAccessor)
    : Ep.Req<GetTeacherScheduleImageRequest>.Res<Results<FileContentHttpResult, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>>
{
    private const string PngContentType = "image/png";

    public override void Configure()
    {
        Get("/tasks/teacher-schedule-image");
        Description(builder => builder.Produces(StatusCodes.Status200OK, contentType: PngContentType));
    }

    public override async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>> ExecuteAsync(
        GetTeacherScheduleImageRequest req,
        CancellationToken ct)
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
                HttpContext,
                StatusCodes.Status404NotFound,
                "Не удалось построить расписание преподавателя на выбранную дату."));
        }

        return TypedResults.File(image, PngContentType, $"teacher_schedule_{req.Date:yyyyMMdd}_{req.TeacherId}.png");
    }
}
