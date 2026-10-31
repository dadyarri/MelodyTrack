using System.Collections.Immutable;

namespace MelodyTrack.Rewrite.Domain;

/// <summary>The appointment and settlement rows to save together in one transaction.</summary>
public sealed record AppointmentAccountingChange(Appointment Appointment, ImmutableArray<AppointmentSettlement> SettlementChanges);
