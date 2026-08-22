using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.CourseEnrollments.Requests;

public class CreateCourseEnrollmentRequest : IValidatableRequest
{
    public required Ulid ClientId { get; set; }
    public required Ulid CourseId { get; set; }
}
