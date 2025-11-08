using Facet;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Services.Responses;

[Facet(typeof(Service), Include = [nameof(Service.Id), nameof(Service.Name)])]
public partial class LookupServicesDto;