# Browser Storage Policy

MelodyTrack is online-only. The server is authoritative for every business
record; local storage may preserve unfinished user input, but it must never
represent a completed operation or trigger automatic submission.

## Storage inventory

| Data | Location | Policy |
| --- | --- | --- |
| Access token | Memory | Recreated through refresh after reload |
| Refresh token | Secure, HttpOnly cookie | Rotated by the backend; never exposed to application JavaScript or browser storage |
| Theme | `localStorage`, `melodytrack.theme` | Non-sensitive device preference |
| Saved client identities | `localStorage`, `melodytrack.savedClientIdentities` | Versioned, validated display metadata plus an opaque server-issued reference; never the invitation URL, PIN, access token, or refresh token |
| Durable form drafts | IndexedDB `drafts` | User-partitioned, versioned, runtime-validated, and expired 30 days after the last write |
| React Query data and reference labels | Memory | Server data is never presented as an offline working set |
| Chunk retry/navigation intent | `sessionStorage` | Small, non-sensitive, one-tab recovery markers |
| Static assets | Browser HTTP cache | The minimal Web Push service worker does not intercept fetches or maintain an application-shell cache |

IndexedDB version 2 deletes the former `offlineCommands` and
`offlineIdMappings` stores. Existing commands and mappings are deliberately
discarded and cannot be replayed. Valid version-1 form drafts are upgraded;
queue data is not migrated.

## Mandatory rules

1. Application code does not call browser persistence APIs ad hoc.
2. `shared` owns generic database and durable-form mechanics. Business keys,
   schemas, codecs, baselines, mutations, and invalidation stay with their
   owning slice.
3. Draft records contain a schema version, stable authenticated-user owner,
   timestamps, expiry, and runtime-validated values. Edit drafts also use an
   entity-scoped key and authoritative baseline where one exists.
4. A stale edit draft is not applied over newer server state automatically.
   The user may deliberately reapply or discard it.
5. Passwords, OTP and recovery codes, invite/portal/reset tokens,
   authentication forms, destructive confirmations, search fields, and
   URL-owned filters are never persisted.
6. Validation errors, request errors, modal close, navigation, refresh, and
   browser restart retain eligible drafts. Only confirmed server success or
   explicit discard clears them.
7. Storage failures are visible. Pending or failed writes protect unload and
   navigation; a draft is never described as saved when persistence failed.
8. Drafts never submit themselves and never fabricate domain entities.
9. Account switching cannot expose one user's drafts to another user.
10. Web Storage and IndexedDB are not security boundaries; do not store
    credentials or secret-bearing URLs in them.
11. The Web Push service worker handles only push display and notification
    navigation. Offline caching, background business writes, and fabricated
    offline state remain out of scope.

## Draft record

```ts
type DraftRecord<TPersisted> = {
  schemaVersion: 2;
  ownerUserId: string;
  key: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  expiresAtUtc: string;
  data: {
    updatedAtUtc: string;
    values: TPersisted;
    entityId?: string;
    baselineVersion?: string | null;
  };
};
```

Draft keys are namespaced by domain and operation, for example
`draft:payments:create` or `draft:payments:edit:<payment-id>`. The IndexedDB
primary key is `[ownerUserId+key]`.

See [Durable forms](./durable-forms.md) for the form inventory and developer
checklist.
