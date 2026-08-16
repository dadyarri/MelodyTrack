# Course Progress Map Brief (Archived)

This is a product-design reference for a future client-facing course-progress view. It is intentionally separate from the delivery roadmap.

## Goal

Present an enrolled course as a read-only, interactive learning map rather than a flat checklist. The experience should make completed, current, available, and blocked themes understandable without exposing CRM editing controls.

## Direction

- Use the real course graph, branches, and dependencies.
- Prefer a bottom-to-top progression layout.
- Make dependencies, prerequisites, and the client’s current position readable at a glance.
- Use a calm, parchment-inspired visual language only if it improves usability; do not trade clarity for decoration.
- Keep the client surface read-only and separate from staff course authoring.

## Technical Constraints

- The graph may use `@xyflow/react` with ELK.js for automatic layout and routing.
- Nodes are not draggable or connectable for clients.
- The view must work on desktop and touch devices, including pan, zoom, fit-to-view, and keyboard-accessible topic details.
- Recompute layout for structural changes, not ordinary progress-state updates unless necessary.

## Status

Deferred. The prior portal progress UI was removed while the product direction is reassessed.
