
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Tasks.Requests;

public class GetTeacherScheduleImageRequest
{
    [FromQuery(Name = "teacherId")]
    public required Ulid TeacherId { get; set; }

    [FromQuery(Name = "date")]
    public required DateOnly Date { get; set; }

    [FromQuery(Name = "timezone")]
    public required string Timezone { get; set; }
}
