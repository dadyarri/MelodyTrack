namespace MelodyTrack.Backend.Api.CalendarSubscriptions.Responses;

public class CalendarSubscriptionResponse
{
    public required Ulid Id { get; set; }
    public required string Token { get; set; }
    public required string Url { get; set; }
    public required string FeedType { get; set; }
}
