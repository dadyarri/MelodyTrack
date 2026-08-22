
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Courses.Requests;

public class GetCoursesRequest
{
    [FromQuery(Name = "search")]
    public string? Search { get; set; }
}
