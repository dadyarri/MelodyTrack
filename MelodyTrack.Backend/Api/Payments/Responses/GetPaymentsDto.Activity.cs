using MelodyTrack.Backend.Api.Common.Responses;

namespace MelodyTrack.Backend.Api.Payments.Responses;

public partial class GetPaymentsDto
{
    public RecordActivityDto? LastActivity { get; set; }
}
