using Facet;
using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Data.Models;

namespace MelodyTrack.Common.Api.Clients.Requests;

[Facet(typeof(Client), nameof(Client.Id), nameof(Client.Patronymic), nameof(Client.Contacts),
    NullableProperties = true,
    GenerateBackTo = false)]
public partial class GetClientsPaginatedRequest : PaginatedRequest;