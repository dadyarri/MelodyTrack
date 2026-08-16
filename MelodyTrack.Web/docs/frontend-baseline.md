# Frontend Refactor Baseline

Captured on 2026-07-25 at the start of the Feature-Sliced Design v2.1
migration.

## Toolchain

- Node.js: 25
- npm: 11
- React: 19
- Vite: 8
- TypeScript native preview (`tsgo`): 7
- Vitest: 4
- Steiger with the Feature-Sliced Design plugin

The CI baseline is `npm run verify`, which runs:

1. Biome formatting and linting plus ESLint auto-fix
2. Steiger architecture and public-asset validation
3. browser-targeted CSS compatibility compilation
4. strict TypeScript checking and unit tests
5. Chromium responsive, visual, and overflow tests
6. WebKit form and overlay interaction tests
7. the Vite production build and bundle-budget validation

CI also runs `npm audit --omit=dev` before verification.

## Architecture Baseline

Steiger passes without layer-direction, sibling-slice, private-import, or
segmentless-slice violations. The remaining scoped configuration covers page
slice granularity and deliberately focused slices that currently have one
consumer; it no longer exempts any legacy feature structure.

## Automated Behavior Baseline

The current suite contains 28 tests covering:

- normalization and rejection rules for public API configuration;
- complete and partial authentication session persistence;
- allowed and denied protected-route behavior;
- injected HTTP authentication and basic API error normalization.

The next migration stages must expand this baseline before moving HTTP refresh,
durable-form recovery, conflict resolution, or other business-critical behavior.

## Stage 2 Foundation

The app layer is divided by purpose into `entrypoint`, `router`, and `styles`.
Portal theming is composed at the router boundary, so pages no longer import
from the app layer.

Business-agnostic infrastructure now has stable shared public APIs:

- `shared/api` owns HTTP transport, error normalization, and generic conflict
  parsing; authentication injects its session adapter from above;
- `shared/config` owns validated public environment configuration;
- `shared/lib` owns generic date, money, download, draft, polling, pluralization,
  and keyboard helpers;
- `shared/ui` owns generic icons, charts, BBCode UI, summaries, and existing
  reusable primitives.

The former top-level `api`, `components`, and `utils` compatibility locations
were removed in Stage 4 after their consumers migrated.

## Stage 3 Page Composition

All route screens use page slices, and the old mixed-purpose `layout` directory
has been removed. Authorization gates now live with router composition in
`app/router`.

Substantial reusable or independently maintained sections now have explicit
widget boundaries:

- `app-shell` owns staff navigation and shell presentation;
- `client-portal-shell` owns the portal frame;
- `client-history` owns the history drawer and timeline panel;
- `schedule-calendar` owns the interactive calendar;
- `course-workspace` owns the lazy-loaded course graph, authoring, and progress
  surface, leaving the course route page as composition only.

Theme composition and lazy-chunk recovery moved to `shared` because widgets
need them and may not import upward from `app`. Every widget exposes a root
public API, and Steiger reports no layer-direction, cross-slice, or private
import violations.

## Stage 4 Entity Migration

The client domain is the first complete entity boundary. `entities/client` now
owns client contracts, CRUD and history requests, debtor export, portal and
calendar-link operations, stable React Query keys, contact helpers, and its
public API. Client consumers no longer import those concerns from
`api/crm.ts`, `api/types.ts`, or `api/queryKeys.ts`.

Generic transport contracts such as ULIDs, pagination, create responses, and
record activity metadata moved to `shared/api`. Client API tests preserve
idempotency headers, endpoint paths, response mapping, and the pre-migration
query-key shapes.

The appointment domain now owns schedule contracts, appointment requests,
recurrence lookups, and schedule query keys. Its nested client, service,
provider, and course-theme contracts mirror the purpose-specific DTOs exposed
by the backend instead of importing sibling entities. The generated backend
OpenAPI document is the contract source of truth; validation also corrected
appointment deletion to send `scope` and `expectedActivityId` in the JSON body
defined by `DeleteAppointmentRequest`.

Stage 4 is complete. Cohesive entity slices now own services, courses and
enrollments, payments, expenses, users and availability, recurring tasks,
reference books, dashboard analytics, audit records, and session state in
addition to clients and appointments. Their API contracts and query keys are
exposed only through slice public APIs.

Reusable client, service, user, role, expense-category, and client-source
selectors live with their entities. Appointment status representation, user
availability rules, task labels, and dashboard date-range query helpers moved
with the domain they represent.

Route-specific controllers now live in page or widget `model` segments.
Reusable actions are explicit feature slices, including client management,
appointment management, payment recording, course enrollment, course-progress
updates, user editing, and reference-book management. Authentication UI actions
are separated from the session entity, and onboarding and portal access have
segmented public APIs.

The monolithic `crm.ts`, `types.ts`, and global query-key registry no longer
exist. The legacy segmentless-feature allowlist was removed, and Steiger
reports no architecture violations.

## Online-only execution and durable forms

Offline commands, replay, temporary IDs, optimistic local domain records,
queue UI, and the application-shell service worker were removed. IndexedDB
version 2 deletes old queue stores without replaying or migrating their data.
Normal mutations succeed only after the server confirms them.

Meaningful business forms use the shared `useDurableForm` adapter. Drafts are
user-partitioned, versioned, runtime-validated, ordered, expiring recovery data;
they never submit automatically or masquerade as server records. See
[`durable-forms.md`](./durable-forms.md).

Authentication state is isolated behind the session adapter consumed by the
shared HTTP transport. Access tokens are memory-only, refresh rotation and
logout propagate across tabs, and legacy persisted access tokens are removed.
The backend still accepts refresh tokens in JSON request bodies, so the
remaining refresh-token `localStorage` risk and coordinated HttpOnly-cookie
migration remain documented in the browser-storage policy.

## Mobile browser baseline

The supported baseline is iOS Safari and Safari 16.4+, Chrome and Edge 109+,
and Firefox 115+. Browserslist, Autoprefixer, the Vite JavaScript/CSS targets,
Lightning CSS minification, and source compatibility compilation enforce the
same matrix.

The shared visual-viewport compatibility layer handles dynamic viewport
dimensions, software-keyboard offsets, safe areas, contained overlays,
compact 16px form text, and primary 44px touch targets. Responsive browser
tests cover authentication, list, analytics, schedule, course workspace,
profile, and portal route families at 320, 375, 390, and 430px portrait plus
compact landscape. Chromium owns viewport and visual regression coverage;
WebKit owns modal, form, picker, dropdown, and drawer interaction coverage.
The ongoing physical-iPhone release checklist is documented in
[`mobile-browser-support.md`](./mobile-browser-support.md).

## Production Bundle Baseline

The production build succeeds with route-level code splitting. The largest
generated JavaScript assets at the start of the migration are:

| Asset group | Raw size | Gzip size |
| --- | ---: | ---: |
| main index chunk | 383.10 kB | 119.84 kB |
| courses route | 342.71 kB | 111.97 kB |
| date utilities | 246.17 kB | 64.77 kB |
| React runtime | 189.64 kB | 59.65 kB |
| date picker | 167.46 kB | 36.41 kB |

These figures are a comparison point, not permanent budgets. Stage 6 should
inspect bundle composition and prevent course graph, rich-text editor, chart,
and other route-specific dependencies from leaking into unrelated navigation
paths.

## Configuration Baseline

`VITE_API_BASE_URL` is validated before the HTTP client is created. It accepts
an HTTP(S) URL for a separate API origin or a root-relative path for same-origin
deployments.

Local development uses `/api`; Vite proxies that prefix to
`MELODY_TRACK_API_PROXY_TARGET` (default `http://localhost:5000`) and removes
the prefix before forwarding. A phone or other LAN device therefore sends API
requests back through the same Vite origin instead of trying to connect to a
backend port that is bound only on the development machine.

Real `.env` variants are ignored. Copy `.env.example` to an appropriate local
Vite environment file and configure the deployed value through the build
environment. The test runner supplies `/api` without relying on an ignored
file.

## Stage 6 Performance And Usability Closeout

Stage 6 completed on 2026-07-25. The mandatory production budgets now cover
initial raw/gzip/Brotli JavaScript and CSS, the largest JavaScript chunk, and
public assets. The closeout build transfers approximately 1,229 KiB raw /
404 KiB gzip / 379 KiB Brotli of initial JavaScript. The largest JavaScript
chunk is approximately 433 KiB raw.

SCEditor and its theme are loaded only when a course lesson/homework editor is
opened; the main course route is approximately 231 KiB raw and the deferred
editor is approximately 111 KiB. Navigation intent prefetch deduplicates route
module loads and warms only the dashboard and schedule default critical data.

TanStack Query now has explicit freshness, cancellation, polling, pagination,
and mutation invalidation behavior. Durable drafts use validated,
user-partitioned IndexedDB storage. Generic HTTP and secret-bearing Web Storage
caches were removed.

Usability closeout includes URL-backed working state, persistent retryable list
errors, background-refresh feedback, responsive table priorities, a mobile
schedule agenda, accessible icon actions, reduced motion, actionable empty
states, explicit draft persistence state, and navigation protection after a
failed durable save.

The backend now negotiates Brotli/gzip for dynamic JSON and problem-details
responses. Existing entity slices already consume purpose-specific list and
lookup DTOs established in Stage 4. Conditional GET is intentionally deferred:
the authenticated, frequently mutated list endpoints do not yet expose an
authoritative version token, and synthesizing validators in the frontend would
create stale-data risk rather than a transport optimization. Revisit ETags
when backend entity/list version semantics are designed and integration-tested.

The closeout suite contains 76 tests. `npm run verify` passes formatting,
ESLint, Biome, Steiger FSD validation, asset checks, strict type checking,
tests, production build, and bundle budgets.
