using FastEndpoints;

namespace MelodyTrack.Backend.Api.Courses.Requests;

public class UpdateCourseRequest
{
    [BindFrom("id")]
    public Ulid Id { get; set; }

    public Ulid? ExpectedActivityId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public List<CreateCourseLevelRequest> Levels { get; set; } = [];

    public List<CreateCourseBlockRequest> Blocks { get; set; } = [];
}
