using Facet;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Services.Responses;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Schedule.Responses;

[Facet(typeof(Appointment), nameof(Appointment.RecurringRule), NestedFacets = [typeof(LookupClientDto), typeof(LookupServicesDto)])]
public partial class AppointmentDto;