using Facet;
using MelodyTrack.Common.Data.Models;

namespace MelodyTrack.Common.Api.Clients.Responses;

[Facet(typeof(Client), Include = [nameof(Client.Id), nameof(Client.FirstName), nameof(Client.LastName)])]
public partial class LookupClientDto;