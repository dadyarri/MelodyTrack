using Facet;
using MelodyTrack.Common.Data.Models;

namespace MelodyTrack.Common.Api.Services.Responses;

[Facet(typeof(Service), Include = [nameof(Service.Id), nameof(Service.Name)])]
public partial class LookupServicesDto;