# Portal design system

## Appearance preference

The header's “Оформление” control switches between RPG and a muted graphite minimalist
presentation. Custom segments adapt to each palette; native radio inputs preserve
keyboard operation and expose the selected option. Both use the same components and data. Minimal mode removes textures,
ornaments, fantasy icon frames, decorative typography, and gradient bars, including
inside dialogs and the level-progress component. Level and experience remain visible.

`RpgTheme` provides `PortalAppearanceProvider`; dialog surfaces consume the same
context. `shared/config/portal-appearance` owns validated reads/writes of
`localStorage["melodytrack.portalAppearance"]` (`rpg` or `minimal`). Missing or invalid
values default to RPG. Storage events synchronize other tabs. Read/write failures
leave the page usable and show a warning beside the control. The preference is
device-wide and separate from `melodytrack.theme`. The gallery and authenticated
portal now share this boundary.

## RPG presentation

The RPG appearance uses one fixed warm fantasy palette, revised from customer feedback:
dark walnut backgrounds with smooth tonal shading, leather-brown panels,
bronze frames, cream text, and sage, muted plum, copper, and teal accents.
The customer-provided RPG panel reference guides framing and colour density;
Potion Craft remains a reference for warmth and crafted detail. Large lyre imagery
has been removed. System sans-serif body text keeps the interface approachable;
Georgia/Times New Roman are reserved for headings. No new fonts, generated
illustrations, or runtime animation dependencies are introduced.

The additional customer references reinforce a desktop RPG treatment: darker
surroundings, textured title bands, ornate borders, and coloured inventory-style
icon slots. Compact layouts retain the same visual language.

`RpgFrame` provides four sourced corner flourishes for the page boundary.
Panels have inset borders and smaller matching corner ornaments. Cards expose
a presentation-only `accent` prop for textured, contrasting header bands.
`RpgIcon` applies colour gradients to the original SVG masks, with bevelled
inventory-style slots for larger icons. Progress fills share a sage-to-honey gradient in Chromium, Safari, and
Firefox styling. The fixed theme declares `color-scheme: dark` for native controls
and does not follow the administrative theme preference.

## MelodyTrack signature accents

Keep the MelodyTrack name. The tower and “Гитарная гильдия” text in the reference
are examples, not the product identity. The current presentation proposal pairs a
sourced guitar-pick outline with the initials MT (`RpgSignature`). Its compact
form repeats in `RpgDivider`, the main call to action, and success feedback.
This is a composition of existing artwork and typography, not exclusive new
illustration work or an approved replacement logo.

Use these accents sparingly. `RpgButton` has opt-in `plaque`, `ribbon`, and `scroll` shapes;
the scroll uses Chloe Wolfe's ready-made CC0 Scroll UI Container from OpenGameArt.
Its complete silhouette, parchment, and rolled edges come from the original PNG;
An SVG wrapper rotates the unchanged image 90 degrees to place the rolls on the
left and right. CSS border-image stretches the writing area while preserving its
corners in RPG mode. The caption uses bold, dark serif text over the light paper.
ordinary utility actions keep the standard shape. `RpgPanel surface="parchment"`
provides a locally scoped light material with dark text for an occasional note,
while most panels retain the dark palette. Never use these treatments to imply
new rewards, character classes, or achievement mechanics.

## Gallery

`RpgLevelProgress` displays an experience bar with a pick-shaped level shield,
gold segmented fill, and a pointed bronze frame. Pass `level`, `value`, and `max`
(experience within the current level). These are caller-owned presentation values;
the component does not calculate levels or grant experience. The gallery uses
level 3 and 420/1000 as fixtures. Native progress semantics expose the amount,
and the ordinary progress components remain available for course completion.

Start Vite with `npm run dev` and open `/dev/portal-design`. This route is gated by
`import.meta.env.DEV`, lazy loaded, and omitted from production routing. It uses
fixtures, does not require portal authentication, and does not call product APIs.
The surrounding application retains its ordinary session bootstrap.

The gallery demonstrates navigation, player identity, cards, progress, dialogs,
pending/disabled buttons, loading, empty, success, and normalized API error states.
Try the appearance switch, completion action, error retry, and dialog controls.
SVG author/source/licence credits are visible in its footer.

## Ownership and composition

- `shared/ui/rpg`: `RpgTheme`, `RpgFrame`, tokens, decorative SVG icons, buttons, panels,
  badges, cards, progress, state blocks, API errors, navigation, and dialogs.
- `entities/player-alias`: public alias representation.
- `entities/course`, `entities/homework`, `entities/trainer`: passive cards.
- `entities/leaderboard`: ranked entries; callers supply order and formatted
  results. This is a frontend presentation model, not a server DTO or scoring API.
- `widgets/portal-header`: brand, identity, navigation, and action slots.
- `app/devtools`: development-only component catalogue, fixtures, and local
  demonstration state. This tooling surface is separate from product page slices.

Import primitives from `@/shared/ui/rpg` and domain components through their slice
public APIs. Wrap new portal content in `RpgTheme`. Styles and tokens are scoped to
that boundary. Alias selection and access errors retain `ClientPortalThemeProvider`;
the authenticated shell uses `PortalControlsThemeProvider` to align existing
notifications, vacation controls, and calendar dialogs with its selected appearance.
Pass router links into `RpgNavigation`; wrap their text and icon in `RpgNavItem`.
Use `aria-current="page"` on the active route link. The gallery uses `location`
because its links navigate within the page.

The header skip link expects `<main id="portal-content" tabIndex={-1}>`.
Cards accept action slots so navigation, authorization, and mutations stay with
their owners. Progress is display-only. Cards do not invent completion rules,
enrollments, points, rewards, or trainer engines.

## Dialogs and feedback

`RpgDialog` uses native `showModal()` for modal focus handling and background
inertness. Control it with `open` and `onRequestClose`; place actions in `footer`.
The first enabled control receives initial focus. Escape and the close button
request dismissal; `pending` blocks both during an operation. Parent actions
must also disable their own submission buttons while pending. Close through the
controlled `open` prop rather than a nested `method="dialog"` form.

Dialogs stay in the theme boundary and the browser top layer; they do not inherit
administrative styling through an Ant Design overlay. Their bounded scrolling
uses the application's visual viewport and safe-area variables.

`RpgApiError` reuses normalized errors and the existing text-copy helper. Trace
IDs remain visible and selectable when copying fails. Do not pass credentials
or portal URLs as diagnostic text.

## Motion, accessibility, and assets

Shared tokens specify 140ms control feedback, 280ms reveals/completion feedback,
and 1600ms decorative loading shimmer. Minimal mode uses an 800ms rotating loading
indicator. Reduced motion removes the effects while
retaining text, state colours, and progress values. Actions never depend on
animation completion. Controls have visible focus and at least 44px height.

Source icons/ornaments are unmodified files by Lorc, licensed under CC BY 3.0.
The former dotted background pattern has been removed throughout the portal and gallery.
Corner Flourish 3 is by Arvin61r58 under CC0, obtained through FreePNGimg. Sources and licence links
are listed in the gallery footer. Preserve those
credits when integrating the system into the live portal in Stage 23. Asset
imports follow Vite's fingerprinted bundle handling.

## Verification status

Focused unit and Chromium/WebKit browser scenarios accompany the components.
They cover pending actions, form semantics, bounded progress, dialog focus and
dismissal, reduced motion, short viewports, long aliases, and fixed-theme isolation.
The authorized `npm run verify:fix` pipeline passed, including unit, Chromium, WebKit, type, architecture, security-boundary, production-build, and bundle-budget checks. Chromium and WebKit scenarios cover the gallery at desktop and compact widths.

## Live portal integration

`/portal` now renders the home page instead of redirecting to the schedule.
`app/router/PortalLayout` composes the header and shell widgets, preserving their
FSD boundaries. The existing access and alias guards protect every nested route.
Header navigation exposes home, courses, homework, trainers, leaderboards, schedule,
and vacations. Logout, notifications, push controls, and system notices remain available.

Home loads schedule and enrollments through the existing client-scoped query keys.
Schedule retains its refresh and calendar-subscription behavior with RPG presentation.
Query cancellation propagates to the HTTP boundary. API failures show retry and trace
information; failed loads are not presented as empty course or homework lists.

Courses show assigned course names, descriptions, and programme outlines. Homework
shows nonempty BBCode assignments only for themes in WaitingForHomework (4), without
submission or completion actions. Progress maps and lesson-material access remain
deferred. Upcoming trainer and leaderboard pages contain no fabricated scores or XP.

The implementation and its added tests have passed frontend verification. Review both appearances with a real client session as part of normal acceptance.

## Compact navigation and portal controls

The header menu is the single cross-section navigation. Home and learning pages
show content without repeating section links or upcoming-section cards. The header
uses a compact two-icon appearance selector (guitar and layout), a notification icon, and an icon-only logout
button, all with accessible names and keyboard focus.

Portal notifications reuse the existing query/mutation hook with an RPG dialog,
request errors, unread count, and themed push controls. Staff notifications retain
their existing presentation. Vacation requests reuse the same mutation controller
and versioned cancel operation with a portal form and history. Native local date-time
inputs convert through Dayjs to the existing UTC request contract; values are kept
on failure and cleared only after successful creation. Neither theme uses the old
dotted SVG pattern. Automated verification remains pending.
