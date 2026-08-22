---
name: melodytrack-frontend-feature
description: Place and implement MelodyTrack React frontend features within its Feature-Sliced Design, generated API, TanStack Query, browser persistence, durable-form, and mobile-browser boundaries. Use for non-trivial UI features or frontend refactors; do not use for generated Kiota code or backend-only contract work.
---

# MelodyTrack Frontend Feature

Work from the MelodyTrack repository root. Read [the repository guidance](../../../AGENTS.md), [the frontend guidance](../../../MelodyTrack.Web/AGENTS.md), and the existing code around the feature before choosing a structure.

## Place code by ownership

Use [the FSD development guide](../../../MelodyTrack.Web/docs/fsd-development-guide.md) to choose the lowest honest Feature-Sliced layer:

- `pages` compose routes;
- `widgets` compose substantial reusable page sections;
- `features` own reusable user actions;
- `entities` own business representations and entity behavior;
- `shared` owns business-agnostic infrastructure and UI.

Imports point downward. Same-layer slices do not import one another; compose higher or move genuinely shared behavior lower. Cross-slice consumers use intentional root `index.ts` public APIs, while files inside a slice use relative imports. Do not create an abstraction or slice until it has truthful ownership and more than decorative value.

## Keep API and state boundaries

- TanStack Query owns server state. Preserve existing query keys, invalidation, cancellation, optimistic behavior, idempotency headers, download handling, and error semantics.
- Generated Kiota request/response models are the server-contract source of truth. Never edit `src/shared/api/generated/` or add handwritten DTO mirrors.
- Keep application-facing API/query wrappers in the owning slice so components and hooks are not coupled directly to generated request builders.
- Axios is transitional. Do not create new Axios infrastructure; when touching transport code, inspect the current migration boundary and preserve behavior until the owning path is cut over.
- Keep form state, view models, component props, and persistence schemas handwritten when they are frontend-owned rather than server contracts.

## Load conditional project guidance

- For any browser persistence, read [the browser storage policy](../../../MelodyTrack.Web/docs/browser-storage-policy.md). Never persist reusable credentials, tokens, PINs, or portal links.
- For reload-surviving user input, read [the durable forms guide](../../../MelodyTrack.Web/docs/durable-forms.md); preserve schema versioning, user scoping, validation, and explicit eligibility.
- For browser APIs, compact/mobile UI, downloads, clipboard, sharing, focus, or WebKit behavior, read [the mobile-browser guide](../../../MelodyTrack.Web/docs/mobile-browser-support.md). User-activation APIs must be invoked before the first asynchronous boundary and need a user-operable fallback.

## Add tests at the owning boundary

- Colocate tests with the source or slice. Use `.test.ts`/`.test.tsx` for unit or jsdom behavior, `.browser.test.tsx` for behavior needing a real browser, and `.webkit.test.tsx` when WebKit is a specific risk. Browser-suffixed tests may participate in multiple browser lanes.
- Name `describe` after the subject or capability. Write `it` text as a lower-case present-tense behavioral sentence, such as `it("preserves the replay key when creating an appointment", ...)`.
- Keep one observable behavior per test and arrange, act, then assert without redundant phase comments. Prefer accessible/user-visible queries and public state over DOM structure or internal hook details.
- Keep setup local; put helpers in `src/test/` only when multiple suites share them. Restore fake timers, storage, global browser APIs, and manual mocks so tests remain order-independent.
- Test application-facing API wrappers for request mapping, query keys, invalidation, cancellation, and normalized errors. Do not test or snapshot generated Kiota internals.
- Use real-browser coverage for user activation, clipboard/share/download, focus, viewport, touch, and other semantics jsdom cannot reproduce. Cover the explicit fallback as well as the successful capability path.

## Verify by affected boundary

Inspect current scripts in [package.json](../../../MelodyTrack.Web/package.json). Use focused type, unit, architecture, browser, or bundle checks according to the change; visual and interaction changes normally need browser inspection at relevant viewport sizes. Full `verify:fix` is mutating and requires the repository’s authorization condition. Review any formatter/linter mutations before keeping them.
