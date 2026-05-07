using Facet;
using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Clients.Requests;

[Facet(typeof(Client), nameof(Client.Id), nameof(Client.Patronymic), nameof(Client.Contacts),
    NullableProperties = true,
    GenerateToSource = false)]
public partial class GetClientsPaginatedRequest : PaginatedRequest
{
    [BindFrom("search")]
    public string? Search { get; set; }
}
