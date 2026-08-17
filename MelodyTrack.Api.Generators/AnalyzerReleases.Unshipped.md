; Unshipped analyzer release

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
MTAPI001 | MelodyTrack.Api | Error | Endpoint classes must use the required suffix.
MTAPI002 | MelodyTrack.Api | Error | Endpoint classes must declare a handler.
MTAPI003 | MelodyTrack.Api | Error | Endpoint classes must declare exactly one handler.
MTAPI004 | MelodyTrack.Api | Error | Endpoint handlers must be public and static.
MTAPI005 | MelodyTrack.Api | Error | Endpoint handlers must accept cancellation.
MTAPI006 | MelodyTrack.Api | Error | Endpoint operation IDs must be unique.
MTAPI007 | MelodyTrack.Api | Error | Endpoint method and route pairs must be unique.
MTAPI008 | MelodyTrack.Api | Error | Endpoint routes must be valid application-relative routes.
MTAPI009 | MelodyTrack.Api | Error | Endpoint methods must be supported.
