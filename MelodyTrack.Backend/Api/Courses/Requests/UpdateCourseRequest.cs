using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Courses.Requests;

public class UpdateCourseRequest : IValidatableRequest
{
    [JsonIgnore]
    public Ulid Id { get; set; }

    public Ulid? ExpectedActivityId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public List<CreateCourseLevelRequest> Levels { get; set; } = [];

    public List<CreateCourseBlockRequest> Blocks { get; set; } = [];
}
