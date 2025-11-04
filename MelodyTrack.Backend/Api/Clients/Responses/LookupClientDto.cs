using Facet;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Clients.Responses;

[Facet(typeof(Client), Include = [nameof(Client.Id), nameof(Client.FirstName), nameof(Client.LastName)])]
public partial class LookupClientDto
{
    
}