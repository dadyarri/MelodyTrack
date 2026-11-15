---
name: ui-library-first
description: Design, implement, or refine frontend UI using existing component libraries instead of generic hand-built AI UI. Use for React web apps, component selection, UI redesigns, dialogs, menus, toasts, drawers, command palettes, animated numbers, and visual polish. Preserve the project's existing stack and design system; do not use for backend-only tasks or unrelated code changes.
---

# UI Library First

Build interfaces with an intentional visual identity and reliable interaction behavior. Compose proven components before writing custom replacements. A library supplies building blocks, not the visual identity of the product.

## 1. Inspect before choosing

- Read project instructions, dependency manifests, lockfiles, existing UI components, styles, theme tokens, and any design specification.
- Identify the framework, package manager, existing primitive library, styling approach, and target interaction. Reuse working components and conventions.
- Determine whether the task needs a foundation primitive, a specialist interaction, or a designed visual component. It may need a combination of these layers.
- Do not migrate frameworks, replace a functioning component library, or introduce a new design system merely to apply this skill. If the project is not React-based, use equivalents appropriate to its existing stack; the React catalog below is not a reason to introduce React.

## 2. Choose the appropriate layer

### Foundations: everyday UI

Use the existing foundation for buttons, form controls, dialogs, popovers, menus, selects, and other standard interfaces. Prefer tested primitives for focus management, keyboard navigation, semantics, and accessibility over implementing them from scratch.

| Library | When to use | Documentation |
| --- | --- | --- |
| shadcn/ui | Editable, styled component source for an application foundation | https://ui.shadcn.com |
| Base UI | Unstyled React primitives when custom styling is required | https://base-ui.com |
| Radix Primitives | Accessible unstyled React primitives, particularly when already used by the project | https://www.radix-ui.com |

Choose one coherent foundation rather than installing all three. When using shadcn/ui, inspect its existing configuration and underlying primitive implementation before adding components. Do not replace Base UI with Radix, or vice versa, without a project-specific reason.

### Specialists: interactions with subtle behavior

Prefer a focused library when a standard primitive does not cover a complex interaction well. Keep specialists visually consistent with the foundation.

| Task | Preferred library | Documentation |
| --- | --- | --- |
| Toasts and notifications | Sonner | https://sonner.emilkowal.ski |
| Gesture-driven drawers and bottom sheets | Vaul | https://vaul.emilkowal.ski |
| Searchable command palettes | cmdk | https://cmdk.paco.me |
| Animated numeric values | NumberFlow | https://number-flow.barvian.me |

Check existing dependencies first. Reuse an existing equivalent if it works; do not add a parallel library for the same purpose without a clear reason. Consult current official documentation and verify compatibility, maintenance status, license, and installation instructions before adding a dependency.

For a task outside this catalog, use the installed `pick-ui-library` skill by Emil Kowalski if available and appropriate. Its repository is https://github.com/emilkowalski/skills. Otherwise select a suitable library using project requirements and official documentation. This skill does not require the external skill to be installed.

### Designed components: targeted visual polish

Use finished component designs when the product needs a distinctive visual element, rather than generating every effect from scratch.

| Library | Suitable use | Website |
| --- | --- | --- |
| Beautiful UI | AI product interface patterns, including agent states and interactions | https://beautifului.dev |
| RareUI | Expressive animated controls and visual effects | https://rareui.in |
| Magic UI | Animated React components and effects that complement shadcn/ui | https://magicui.design |
| Aceternity UI | Elaborate visual treatments and expressive landing-page sections | https://ui.aceternity.com |
| 21st.dev | Discovering existing component designs and implementations | https://21st.dev |

Select a particular component that serves the product, not an entire gallery's visual language. Inspect copied code, dependencies, accessibility, licensing, responsiveness, and performance. Adapt its styling and API to the application; do not treat registry code as automatically production-ready.

## 3. Make the foundation look intentional

Default component styling is not a design direction. When the task includes visual design or redesign:

- Establish or reuse a small set of coordinated design tokens for typography, color, border radius, spacing, borders, and elevation.
- Prioritize typography, palette, and corner radius as the main levers for changing the default shadcn appearance.
- Choose a visual direction appropriate to the product, not a universal preset. For example, an editorial interface may use restrained corners and serif headings; a utilitarian dashboard may favor compact, readable controls.
- Apply decisions consistently through the existing theme and shared components, including supported dark mode. Avoid isolated hard-coded styles that undermine the system.
- Keep familiar interaction patterns even when the visual treatment changes.
- Preserve an established product identity unless a redesign has actually been requested.

## 4. One standout per page

When adding expressive designed components, designate at most one primary visual focal point per page: an unusual hero, distinctive interaction, or prominent animated element.

- Give that element a clear purpose and keep surrounding components quiet and functional.
- Do not stack decorative animated backgrounds, glowing cards, moving borders, animated headlines, and multiple competing effects just because they are available.
- A page may have no standout if its purpose is routine work. Ordinary interaction feedback, loading indicators, and functional numeric transitions are not automatically decorative focal points.
- Respect reduced-motion preferences and avoid animation that interferes with reading or completing a task.

## 5. Implement deliberately

1. Identify the components and relevant states required by the user flow.
2. Reuse project components; then select an appropriate foundation, specialist, or designed component only for unmet needs.
3. State significant dependency or design decisions briefly. Follow the project's package manager and the selected library's current official setup instructions. Use the existing theme and shared wrappers.
4. Implement the requested feature, including real state and event handling. Do not substitute visual mockups for working interactions unless a mockup is requested.
5. Where justified, implement a custom component when existing options cannot meet the requirements; explain the trade-off instead of forcing a library.

Examples of concrete choices:

- A settings page: existing foundation inputs and dialogs; no decorative centerpiece needed.
- A command palette: cmdk with project-specific commands, keyboard shortcuts, and a dialog from the existing foundation.
- Notifications: Sonner integrated with the application theme rather than a bespoke stack of timed DOM elements.
- Mobile filter sheet: existing drawer or Vaul, with working dismissal, focus behavior, and accessible labels.
- Landing page: standard foundation controls and one selected designed component as its focal point.

## 6. Validate the result

Use the project's existing tests and available browser tools. Verify relevant keyboard navigation, focus restoration, accessible names, responsive layout, color contrast, reduced motion, and dark mode where supported. Exercise loading, empty, error, disabled, and long-content states where applicable. For specialist components, verify the behavior that justified using them (for example, toast stacking, drawer dragging and dismissal, or keyboard selection in a command palette).

Run appropriate type checks, lint, tests, and build commands. Report actual verification and any remaining limitations; do not claim visual or interaction checks that were not performed.

## Provenance

Based on Griffin Wooldridge's video, *UI Libraries for Claude Code (Ban the AI Look)*: https://youtu.be/zsCGS-Yzjnw. The three-layer library taxonomy, named library catalog, emphasis on customizing foundation styling, specialist behavior, and one-standout principle originate from the video. Repository inspection, framework compatibility, dependency safeguards, accessibility validation, examples, and implementation checks are additional engineering instructions to make the advice usable by a coding agent.
