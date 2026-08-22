using Microsoft.AspNetCore.Mvc;
using Facet;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Clients.Requests;

[Facet(typeof(Client), nameof(Client.Id), nameof(Client.Patronymic), nameof(Client.Contacts), nameof(Client.Source),
    nameof(Client.Vacations), nameof(Client.Appointments),
    NullableProperties = true,
    GenerateToSource = false)]
public partial class GetClientsPaginatedRequest : PaginatedRequest
{
    [FromQuery(Name = "search")]
    public string? Search { get; set; }
    [FromQuery(Name = "lifecycleStatus")]
    public ClientLifecycleStatus? LifecycleStatus { get; set; }
}
