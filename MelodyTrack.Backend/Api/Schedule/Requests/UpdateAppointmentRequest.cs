
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Schedule.Requests;

public class UpdateAppointmentRequest
{
    [JsonIgnore]
    public Ulid Id { get; set; }
    public Ulid? ClientId { get; set; }
    public Ulid? ServiceId { get; set; }
    public Ulid? ProviderId { get; set; }
    public Ulid? CourseThemeId { get; set; }
    public bool HasCourseThemeSelection { get; set; }
    public Ulid? RecurrenceTypeId { get; set; }
    public string? LessonNotes { get; set; }
    public bool HasLessonNotes { get; set; }
    public DateTime? StartDate { get; set; }
    public string? Timezone { get; set; }
    public string? Status { get; set; }
    public string? Scope { get; set; }
    public int? RecurrencePattern { get; set; }
    public Ulid? ExpectedActivityId { get; set; }
}
