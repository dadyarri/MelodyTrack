using Facet;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Services.Requests;

[Facet(typeof(Service), Include = [nameof(Service.Name)])]
public partial class GetServicesPaginatedRequest: PaginatedRequest;