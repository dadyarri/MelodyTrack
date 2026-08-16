# Frontend Verification Inventory

The frontend verification baseline remains owned by `MelodyTrack.Web/package.json`. Run scripts from `MelodyTrack.Web/`; monorepo CI sets that working directory explicitly.

| Script | Classification | Purpose and migration constraint |
| --- | --- | --- |
| `check-build-budget.mjs` | Generally useful | Measures initial raw, gzip, and Brotli JS/CSS plus the largest JS chunk. Keep it after Kestrel replaces nginx because it guards browser payload size, not the hosting implementation. |
| `check-css-compatibility.mjs` | Generally useful | Compiles source CSS against the configured Browserslist matrix. It depends on the frontend working directory and must remain aligned with the supported-browser policy. |
| `check-public-assets.mjs` | Generally useful | Enforces per-file and aggregate public asset budgets and rejects raster data embedded in SVG files. It resolves `public/` relative to the script. |
| `check-security-baseline.mjs` | Production-host-specific | Currently validates `index.html`, nginx headers, and nginx location inheritance. Preserve it while the old frontend image exists; replace its nginx assertions with Kestrel/static-host integration coverage during the unified-runtime migration. |
| `check-url-copy-boundary.mjs` | Application-specific, environment-independent | Scans frontend source for reviewed clipboard usage and explicit URL-copy modal producers. Keep it unless the corresponding UX/security boundary is deliberately redesigned. |
| `run-webkit-tests.mjs` | Environment-specific wrapper | Runs WebKit directly on ordinary environments but uses the pinned Playwright container on Arch Linux. CI uses the direct non-container path after installing WebKit; local Arch development requires Docker. |

Additional repository-specific checks remain part of `npm run verify`: Biome formatting/lint, ESLint, Steiger architecture rules, TypeScript checks, Vitest unit tests, Chromium browser tests, WebKit tests, production Vite build, and the scripts above. `npm audit --omit=dev` remains a separate CI step.
