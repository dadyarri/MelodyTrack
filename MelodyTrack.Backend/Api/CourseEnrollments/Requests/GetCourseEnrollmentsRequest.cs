using FastEndpoints;

namespace MelodyTrack.Backend.Api.CourseEnrollments.Requests;

public class GetCourseEnrollmentsRequest
{
    [BindFrom("clientId")]
    public Ulid? ClientId { get; set; }

    [BindFrom("courseId")]
    public Ulid? CourseId { get; set; }
}
