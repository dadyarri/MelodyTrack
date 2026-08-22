---
name: melodytrack-runtime
description: Run, change, or diagnose MelodyTrack development orchestration, database initialization ordering, Vite proxying, unified Kestrel SPA hosting, health routes, publish output, or the production container. Do not use for ordinary endpoint or UI feature work with no runtime impact.
---

# MelodyTrack Runtime

Work from the MelodyTrack repository root. Read [the repository guidance](../../../AGENTS.md) plus [the backend](../../../MelodyTrack.Backend/AGENTS.md) or [frontend](../../../MelodyTrack.Web/AGENTS.md) guidance for the files in scope.

## Keep the two runtime shapes distinct

Development uses [AppHost.cs](../../../MelodyTrack.AppHost/AppHost.cs): PostgreSQL becomes ready, Init completes successfully, Backend becomes healthy, and then Vite starts with a proxy reference to Backend. AppHost and the Aspire Dashboard are development orchestration, not production dependencies or the normal integration-test harness.

Production is one application image. [docker-entrypoint.sh](../../../MelodyTrack.Backend/docker-entrypoint.sh) runs `MelodyTrack.Init --mode production` before executing Backend. Backend serves the API, static SPA, health endpoints, and infrastructure namespaces; the final image must not require Node.js or nginx.

When changing initialization, read [the Init documentation](../../../MelodyTrack.Init/README.md), [the Init entrypoint](../../../MelodyTrack.Init/Program.cs), and [the data project](../../../MelodyTrack.Data/). Keep production, development, and test modes explicit. Backend must not start when required initialization fails.

## Preserve HTTP boundaries

- Vite proxies the real `/api` prefix during development; do not create a second development-only route contract.
- Kestrel owns production static files and SPA fallback. Fingerprinted assets may be immutable; `index.html` and service-worker entry files must remain revalidating/no-cache.
- Unknown API, `/otel`, health, OpenAPI, or other infrastructure paths must never fall through to SPA HTML.
- Preserve security headers, compression, Problem Details for missing API routes, download/no-store behavior, and sensitive auth response cache protection in [UnifiedRuntimeExtensions.cs](../../../MelodyTrack.Backend/Hosting/UnifiedRuntimeExtensions.cs).
- Public URL configuration describes the browser-visible origin. Do not infer it from an internal container, proxy, or Aspire address.

## Diagnose in dependency order

For startup failures, inspect PostgreSQL readiness, Init exit/logs, Backend configuration and `/health`, then Vite proxy or SPA behavior. Do not mask an Init failure with Backend retry loops or duplicate startup migration logic.

For publish/container changes, inspect [Frontend.targets](../../../build/Frontend.targets), [the Dockerfile](../../../MelodyTrack.Backend/Dockerfile), the entrypoint, [verify-unified-image.sh](../../../scripts/verify-unified-image.sh), [UnifiedRuntimeHostingTests.cs](../../../MelodyTrack.Backend.Tests/UnifiedRuntimeHostingTests.cs), and the matching lane in [ci.yml](../../../.github/workflows/ci.yml). Keep project-level builds scoped and root/publish cross-stack behavior intentional.

Use the smallest relevant run or HTTP check while diagnosing. Container verification creates temporary Docker resources and must remain self-cleaning. Follow repository authorization and verification rules before starting services, builds, tests, or image operations.
