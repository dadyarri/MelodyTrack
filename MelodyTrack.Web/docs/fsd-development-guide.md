# Feature-Sliced Development Guide

MelodyTrack uses [Feature-Sliced Design v2.1](https://feature-sliced.design/docs/get-started/overview).
This guide is the placement checklist for day-to-day frontend work.

## Choose The Lowest Honest Layer

| Change | Location | Example |
| --- | --- | --- |
| Application provider, router, or global style | `app/<segment>` | `app/router/router.tsx` |
| Route composition and route-only state | `pages/<route>` | `pages/payments/model/usePaymentsPageController.ts` |
| Large independently maintained page section | `widgets/<slice>` | `widgets/schedule-calendar` |
| Reusable user action | `features/<verb-object>` | `features/record-payment` |
| Domain contracts, API, query keys, or passive domain UI | `entities/<domain>` | `entities/appointment` |
| Business-agnostic transport, hook, utility, or UI primitive | `shared/<segment>` | `shared/api/http.ts` |

Do not add `processes` or recreate the removed top-level `api`, `components`,
`layout`, or `utils` directories.

## Import Direction

Imports point only downward:

```text
app → pages → widgets → features → entities → shared
```

A slice never imports a sibling slice from the same layer. Compose siblings in
the next suitable layer above them. Cross-slice imports use the slice root:

```ts
// Good: the feature consumes an entity public API.
import { paymentsApi, paymentQueryKeys } from "@/entities/payment";

// Bad: private segment import across slices.
import { paymentsApi } from "@/entities/payment/api/paymentApi";
```

Files inside one slice use relative imports. Keep each root `index.ts`
deliberate and named; do not add wildcard exports.

## Adding A Page

1. Create `pages/<route>/ui/<Route>Page.tsx`.
2. Keep the page focused on composition. Put route-only orchestration in
   `pages/<route>/model`.
3. Export the page from `pages/<route>/index.ts`.
4. Add a lazy route in `app/router/router.tsx`.
5. If the route is in staff navigation, add its import to the intent-prefetch
   registry. Export `prefetchRouteData(queryClient)` only for a small critical
   default query; do not warm every query on the page.

## Adding A Widget

Use a widget when a section is large, independently maintained, or composes
multiple features/entities. A widget may own `ui`, `model`, and `lib` segments,
but it must not become a second page router.

For example, schedule layout and interaction belong to
`widgets/schedule-calendar`; creating and editing appointments belong to
`features/manage-appointment`.

## Adding A Feature

Name features as user actions (`record-payment`, `manage-client`,
`update-course-progress`). A feature may:

- call entity APIs;
- coordinate mutations and invalidate entity query keys;
- render the modal/form/control that performs the action.

If code merely displays a domain object without performing an action, place it
in the entity instead.

## Adding An Entity Or API

An entity slice commonly contains:

```text
entities/example/
  api/exampleApi.ts
  api/queryKeys.ts
  model/types.ts
  lib/
  ui/
  index.ts
```

Keep endpoint contracts and query keys with the domain that owns them. List
DTOs should be purpose-specific rather than importing another entity's full
model. Treat the backend OpenAPI document as the contract source of truth.

After a mutation, update or invalidate the narrowest stable entity query key.
Paginated queries should retain previous rows where that avoids a loading
flash.

## Adding Shared Code

Shared code must remain business-agnostic:

- `shared/api`: HTTP mechanics and generic transport contracts;
- `shared/config`: validated public runtime/build configuration;
- `shared/database`: IndexedDB mechanics, never domain tables exposed to UI;
- `shared/lib`: generic hooks and pure utilities;
- `shared/ui`: reusable presentation primitives.

If a utility contains words such as appointment, client, payment, or course,
it probably belongs to that entity or feature rather than `shared`.

## Persistence And Performance

Follow [the browser-storage policy](./browser-storage-policy.md). Domain slices
own durable schemas; shared code only supplies mechanics. Do not add ad-hoc
`localStorage` caches.

Every eligible business form uses `useDurableForm`; follow the
[durable-form inventory and checklist](./durable-forms.md). Do not wire Dexie,
timers, hydration refs, modal status, or navigation guards in controllers.

Keep route-only charts, editors, diagrams, and modal bodies behind lazy
boundaries. Use the existing query cache for intent prefetch and the existing
bundle/public-asset budgets instead of adding a second caching or loading
system.

## Before Committing

Add or update focused tests for changed behavior, then run:

```bash
npm run verify
```

Fix Steiger violations in the code. Do not widen architecture exceptions to
permit an upward, sibling-slice, or private import.
