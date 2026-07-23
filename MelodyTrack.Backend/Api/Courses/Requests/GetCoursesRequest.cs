using FastEndpoints;

namespace MelodyTrack.Backend.Api.Courses.Requests;

public class GetCoursesRequest
{
    [BindFrom("search")]
    public string? Search { get; set; }
}
