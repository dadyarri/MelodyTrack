# MelodyTrack HTTP API contract

This document is the default contract for every endpoint. OpenAPI and the integration tests enforce the mechanically checkable parts.

## Success responses

- A single-resource read returns its DTO directly with `200 OK`.
- Paginated reads return `{ data, info }`. `info` contains `page`, `pageSize`, `total`, `hasPrevPage`, and `hasNextPage`; a domain-specific `summary` may be added beside those members.
- Resource creation returns `201 Created`, a `Location` header, and either the created DTO or `{ id }` when returning the full representation is not useful.
- Updates return `204 No Content` when the client does not need a fresh representation. An update may return `200 OK` only when its returned representation is immediately useful to the caller.
- Deletes and representation-free commands return `204 No Content`.
- Downloads declare their real media type and attachment filename. Generated or private downloads return `Cache-Control: no-store, no-cache, max-age=0`.

## Errors

Every HTTP error uses `application/problem+json` and the shared RFC 9457 model. `type`, `title`, `status`, `instance`, `code`, `traceId`, and `errors` are always present; `detail` is occurrence-specific and optional. Clients branch on `type` or `code`, never on human text.

Validation errors use `{ path, code, message }`. `path` is the camel-case request field, `code` is stable machine-readable validation metadata, and `message` is suitable for display. Stale-write conflicts use the `stale-entity` problem type and add `entityType`, `entityId`, and `currentActivity`.

The status matrix is: malformed/binding/shape errors `400`; missing or invalid authentication `401` with `WWW-Authenticate: Bearer`; insufficient permission `403`; missing resources `404`; stale state and idempotency conflicts `409`; invalid domain transitions `422` when the distinction matters; rate limiting `429`; known temporary outage `503`; and unexpected faults `500`. `429` and `503` include `Retry-After` whenever retry timing is known.

Every response carries `X-Trace-Id`. Error bodies repeat it as `traceId`.

## Concurrency and idempotency

Optimistic concurrency remains an explicit DTO/query contract: mutation requests send `expectedActivityId`, and stale requests receive the current activity in a `409` Problem Details response. It is not also exposed as an HTTP `ETag`; clients must use one mechanism only.

Persistent resource-creation requests accept an optional `Idempotency-Key`. Its scope is endpoint plus authenticated caller. A key and payload are retained for 24 hours. Reusing a completed key with the same payload returns the same `201` status, `Location`, and entity identifier without creating another row. Reusing the key with a different payload, or while its first request is still incomplete, returns the `idempotency-conflict` Problem Details response with `409`.

## Representation conventions

JSON uses camel-case names, string enums, ULID strings, ISO-8601 timestamps, and explicit UTC/time-zone fields where local scheduling matters. Money is a JSON number backed by `decimal`; absence is `null`, not a magic value. Do not add wrappers that only repeat the HTTP status.

When adding an endpoint, use the shared problem factory/results, declare every response and non-JSON media type, give the operation a stable ID, and add or update a contract test. Undocumented errors and duplicate operation IDs fail the OpenAPI contract suite.
