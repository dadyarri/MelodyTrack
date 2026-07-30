namespace MelodyTrack.Backend.Api.Reports.Requests;

public sealed class GetReportRequest
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string Timezone { get; set; } = "UTC";
    public Ulid? ProviderId { get; set; }
    public string GroupBy { get; set; } = "month";
}
