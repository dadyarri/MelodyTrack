using Facet;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Payments.Responses;

[Facet(typeof(Client), Include = [nameof(Client.FirstName), nameof(Client.LastName), nameof(Client.Patronymic)])]
public partial class GetPaymentsClientDto;

[Facet(typeof(Service), Include = [nameof(Service.Name)])]
public partial class GetPaymentsServiceDto;

[Facet(typeof(Payment), NestedFacets = [typeof(GetPaymentsClientDto), typeof(GetPaymentsServiceDto)])]
public partial class GetPaymentsDto;