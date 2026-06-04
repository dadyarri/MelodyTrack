using FastEndpoints;

namespace MelodyTrack.Backend.Api.Tasks.Requests;

public class GetTeacherScheduleImageRequest
{
    [BindFrom("teacherId")]
    public required Ulid TeacherId { get; set; }

    [BindFrom("date")]
    public required DateOnly Date { get; set; }

    [BindFrom("timezone")]
    public required string Timezone { get; set; }
}
