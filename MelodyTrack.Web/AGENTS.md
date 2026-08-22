# Frontend Repository Guidelines

## Scope

These instructions apply to the `MelodyTrack.Web` project. The repository-root `AGENTS.md` defines shared workflow, roadmap, verification, Git, and security rules and takes precedence if there is a conflict.

The frontend is part of the MelodyTrack monorepo. Keep these rules scoped to frontend work under this directory.

## Architecture

The frontend follows Feature-Sliced Design v2.1.

Use these layers from highest to lowest:

1. `app`: entrypoints, providers, routing, global configuration/styles.
2. `pages`: route-level composition.
3. `widgets`: large reusable page sections.
4. `features`: reusable user actions that provide business value.
5. `entities`: business models and reusable domain representations.
6. `shared`: business-agnostic UI, API infrastructure, configuration, utilities.

Rules:

- Do not reintroduce deprecated top-level `api`, `components`, `layout`, `utils`, or `processes` groupings.
- Imports may only point downward through the layer hierarchy.
- Slices on the same layer must not import one another. Compose in a higher layer or move a genuinely shared concept lower.
- Use FSD `@x` only for narrow unavoidable entity-to-entity relationships.
- `app` and `shared` may contain technical segments directly; other layers contain business slices with purpose-based segments such as `ui`, `model`, `api`, `lib`, and `config`.
- Every slice has an intentional root `index.ts` public API for cross-slice imports. Use explicit relative imports inside a slice. Avoid wildcard re-exports.
- Put code in the lowest layer that truthfully owns it.
- Do not create empty layers, speculative abstractions, or decorative one-file slices.
- Keep the scoped Steiger exceptions narrow; fix new architecture violations in code instead of broadening suppression.

Follow `docs/fsd-development-guide.md` for concrete placement/import examples.

## API and Server State

TanStack Query remains the server-state manager.

The application API boundary uses Kiota's Fetch stack:

- keep authentication, session refresh, cancellation, credentials, and error normalization in the centralized transport;
- generated Kiota request/response/entity models become the source of truth for API DTOs;
- do not maintain handwritten TypeScript mirrors of generated API contracts;
- handwritten types remain appropriate for frontend-owned form state, UI state, component props, view models, query convenience types, and persistence schemas;
- keep application-facing API wrappers in the owning FSD slice so React/TanStack Query code is not coupled directly to generated request builders everywhere;
- preserve query keys, mutation semantics, invalidation behavior, cancellation, idempotency headers, blob/download handling, and auth-expiry behavior while transport changes;
- meaningful API errors must flow through the shared normalized application-error path once introduced; do not regress to transient toast-only diagnostics.

## Browser Persistence

Follow `docs/browser-storage-policy.md`. New code must not use persistence APIs ad hoc.

- Keep access tokens and transient query/UI state in memory.
- Use `sessionStorage` only for small non-sensitive tab-recovery markers.
- Use `localStorage` only for small non-sensitive device preferences.
- Put durable user work behind the user-scoped, versioned, runtime-validated IndexedDB/Dexie boundary used by the application.
- Never persist passwords, PINs, access tokens, refresh tokens, CSRF secrets, reset links, portal credential URLs, or other reusable credentials in Web Storage/IndexedDB.
- Business persistence schemas belong to their owning entity/feature; shared code may provide mechanics but must not become a global business-data cache.
- Preserve persisted formats while relocating code unless the same change includes an explicit migration and tests.

## Browser and Mobile Support

Follow `docs/mobile-browser-support.md` and keep the documented supported-browser baseline aligned across Browserslist, Vite targets, CSS tooling, and compatibility checks.

- Prefer standards-based fallbacks and capability detection over user-agent checks.
- Keep shared mobile compatibility fixes centralized rather than scattering WebKit-specific workarounds.
- Preserve 16px form-control text, practical touch targets, safe-area handling, bounded internal scrollers, and no document-level horizontal overflow at compact widths.
- Treat WebKit as a distinct behavioral target, especially for user activation, permissions, clipboard, sharing, downloads, focus, virtual keyboard, visual viewport, popup, media, and fullscreen behavior.
- Browser APIs gated by transient user activation must be invoked directly from the user event before an `await`, network request, timer, state transition, or other asynchronous boundary.
- Provide an explicit user-operable fallback when a browser capability is unavailable or rejected.

## Static Hosting and PWA Boundary

During the monorepo/unified-hosting migration:

- Vite remains the development/build tool;
- production Node/nginx runtime is removed only after Kestrel reproduces required caching, compression, security headers, and SPA fallback behavior;
- fingerprinted assets may be immutable/long cached, while `index.html` and service-worker entry files must not be long cached;
- unknown `/api`, `/otel`, health, or other backend infrastructure routes must never fall through to the SPA;
- the development proxy must preserve the real `/api` prefix once the backend migration reaches that step.

## Security and Telemetry

- Keep credential material out of browser persistence, console output, analytics, telemetry, and copied diagnostic text.
- Client portal links are long-lived authentication material; do not persist them after authentication or expose them through diagnostics.
- Browser telemetry is deliberately minimal and must use the same-origin backend relay; the browser must never receive the Aspire OTLP API key/internal dashboard address.
- A copied support trace ID is diagnostic metadata, not a URL workflow. Use the dedicated/general text-copy path rather than URL-copy semantics.
- Do not add source-map publication/symbolication as part of the initial observability refactor.

## Testing

- Colocate tests with the source file or slice they specify; do not create a separate mirrored test tree.
- Use `<subject>.test.ts` or `.test.tsx` for unit/jsdom behavior, `<subject>.browser.test.tsx` for behavior requiring a real browser, and `<subject>.webkit.test.tsx` for WebKit-risk behavior. Browser-suffixed tests may run in more than one browser lane.
- Name `describe` blocks after the subject or user capability. Write `it` descriptions as lower-case, present-tense behavioral sentences that complete “it …”.
- Keep one observable behavior per `it`; multiple assertions are appropriate when they describe the same outcome. Prefer role, label, accessible name, visible state, and public API assertions over DOM structure or implementation details.
- Keep setup local. Share helpers through `src/test/` only when several suites need the same browser/test boundary, and restore timers, storage, mocks, and global API replacements after each test.
- Test TanStack Query wrappers for request mapping, stable query keys, invalidation, cancellation, and errors at the owning slice. Do not test generated Kiota implementation details.
- Use real-browser tests for user activation, clipboard/share/download, focus, viewport, touch, and other behavior jsdom cannot model faithfully. Add or update WebKit coverage when the documented risk applies.

## Verification

Steiger, formatting, Biome, ESLint, strict TypeScript, unit tests, browser checks, security checks, bundle budgets, and production build remain required quality gates for completed frontend work.

However, **do not run the full verification pipeline automatically after intermediate edits**. Follow the workspace-root verification policy and wait for explicit authorization or completion of the current change batch.

When full frontend verification is authorized, run `npm run verify:fix` unless the repository scripts have intentionally changed. It is a mutating verification pipeline: formatting/ESLint fixes may modify files before the remaining checks. Inspect resulting changes before committing. Fix architecture violations in code rather than suppressing them. Use focused/watch tests during an explicitly authorized debugging session where appropriate.
