# Calendar subscriptions

Calendar subscription feeds preserve all past, non-cancelled appointments and
include future events only when their start time is within the rolling UTC
interval `[generated at, generated at + 14 days]`; both boundaries are
inclusive. The feed uses the server clock, so a subscriber's timezone or
request date never changes the future window.

Calendar applications should refresh the subscription regularly. Cancelled
appointments are absent from the next feed; past non-cancelled appointments do
not disappear merely because time has passed. Event UIDs remain stable while an
event is present, so refreshes do not create duplicate entries.
