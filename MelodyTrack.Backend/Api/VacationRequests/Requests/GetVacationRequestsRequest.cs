using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.VacationRequests.Requests;

public sealed class GetVacationRequestsRequest
{
    [FromQuery(Name = "view")]
    public string View { get; set; } = "pending";
}
