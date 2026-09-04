---
name: melodytrack-recurrence
description: Change or diagnose MelodyTrack recurring appointments and recurring operational tasks while preserving materialization, series update/delete, deduplication, delay, timezone, audit, and transaction behavior. Do not use for unrelated Quartz scheduling or one-off appointments with no recurrence impact.
---

# MelodyTrack Recurrence

Work from the MelodyTrack repository root. Read [the repository guidance](../../../AGENTS.md), [the backend guidance](../../../MelodyTrack.Backend/AGENTS.md), and the focused tests before modifying recurrence behavior. Tests are the executable specification for edge cases.

## Classify the subsystem first

MelodyTrack has two related but separate systems:

1. Recurring appointments use `AppointmentRecurrenceRule`, `RecurringAppointmentService`, `RecurringAppointmentMaterializer`, schedule endpoints, update preparation, and deletion services. They create or reshape appointment series.
2. Recurring operational tasks use rules, transient candidates, persisted executions, presentation mapping, and transition services under [the recurring-task services](../../../MelodyTrack.Backend/Services/RecurringTasks/). They represent reminders/actions and their completed, cancelled, or delayed state.

Do not collapse these systems or move candidate, query, transition, template, and presentation responsibilities into one service.

## Recurring appointments

- Preserve idempotent materialization, open-ended series behavior, provider/client association, UTC storage, and recurrence-type semantics.
- Treat recurrence patterns as an established encoded contract. Do not reinterpret daily, weekly, or monthly values without reading the service and focused tests.
- Updating or deleting an occurrence can affect only one occurrence, the remaining future series, or the entire series. Preserve historical occurrences and rule-splitting/shift behavior for the selected scope.
- Consumers such as schedules, dashboards, reports, and calendar subscriptions may materialize appointments before querying. Check these downstream paths when changing materialization horizons or eligibility.
- Relevant specifications include [RecurringAppointmentMaterializerTests.cs](../../../MelodyTrack.Backend.Tests/RecurringAppointmentMaterializerTests.cs), [RecurringAppointmentServiceTests.cs](../../../MelodyTrack.Backend.Tests/RecurringAppointmentServiceTests.cs), [RecurringAppointmentUpdateTests.cs](../../../MelodyTrack.Backend.Tests/RecurringAppointmentUpdateTests.cs), [AppointmentDeletionServiceTests.cs](../../../MelodyTrack.Backend.Tests/AppointmentDeletionServiceTests.cs), and schedule/calendar/report tests.

## Recurring operational tasks

- Candidate generation determines what is currently actionable; processed executions record transitions. Deduplication keys are durable identities, not display strings.
- Preserve rule/type-specific eligibility, recipient identity, vacation filtering, timezone-derived business dates, deterministic ordering, and prepared-message behavior.
- Complete/cancel/delay must reject unknown, stale, or already-processed candidates consistently. Delayed tasks re-enter candidate behavior only according to their stored delay state.
- Custom tasks have specialized transition behavior. Keep it separate from rule-derived transitions.
- Persist a transition and its audit record atomically. Review rollback tests whenever changing save/audit order or transaction scope.
- Relevant specifications include [RecurringTaskServiceTests.cs](../../../MelodyTrack.Backend.Tests/RecurringTaskServiceTests.cs), [RecurringTaskTransitionTests.cs](../../../MelodyTrack.Backend.Tests/RecurringTaskTransitionTests.cs), [RecurringTaskCustomEndpointTests.cs](../../../MelodyTrack.Backend.Tests/RecurringTaskCustomEndpointTests.cs), [RecurringTaskRuleEndpointTests.cs](../../../MelodyTrack.Backend.Tests/RecurringTaskRuleEndpointTests.cs), and [RecurringTaskTemplateRendererTests.cs](../../../MelodyTrack.Backend.Tests/RecurringTaskTemplateRendererTests.cs).

## Time, data, and verification

Use injected `TimeProvider`, capture a single instant for multi-step decisions, store UTC, and convert to the requested timezone only at domain boundaries that require local dates or presentation. Propagate cancellation through EF and service calls.

The shared integration fixture preserves recurrence lookup/rule tables across resets, so tests must not mutate those rows casually. Use `TestDataFactory` recurrence helpers where appropriate. Run the narrow appointment or task test group first when verification is authorized, then widen to downstream consumers only when the changed invariant crosses those boundaries.
