
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.CourseEnrollments.Requests;

public class GetCourseEnrollmentsRequest
{
    [FromQuery(Name = "clientId")]
    public Ulid? ClientId { get; set; }

    [FromQuery(Name = "courseId")]
    public Ulid? CourseId { get; set; }
}
