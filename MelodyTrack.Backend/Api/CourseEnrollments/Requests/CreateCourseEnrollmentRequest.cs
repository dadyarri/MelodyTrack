namespace MelodyTrack.Backend.Api.CourseEnrollments.Requests;

public class CreateCourseEnrollmentRequest
{
    public required Ulid ClientId { get; set; }
    public required Ulid CourseId { get; set; }
}
