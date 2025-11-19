using Facet;
using MelodyTrack.Common.Api.Clients.Responses;
using MelodyTrack.Common.Api.Services.Responses;
using MelodyTrack.Common.Data.Models;

namespace MelodyTrack.Common.Api.Schedule.Responses;

[Facet(typeof(Appointment), nameof(Appointment.RecurringRule), NestedFacets = [typeof(LookupClientDto), typeof(LookupServicesDto)])]
public partial class AppointmentDto;