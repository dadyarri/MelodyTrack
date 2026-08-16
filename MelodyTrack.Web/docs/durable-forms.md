# Durable Forms

`useDurableForm` is the only form-draft integration. It composes the generic
IndexedDB draft repository and owns hydration, codecs, debounced ordered
writes, status, retry, discard, success clearing, navigation protection,
account partitioning, expiry, and stale edit detection. Domain submission and
query invalidation remain in the owning feature, page, or widget.

## Inventory

| Surface | Durable key/identity | Persisted shape |
| --- | --- | --- |
| Clients create/edit and quick create | create key; client ID for edits | identity, contacts, source, birth date |
| Client vacations | client ID | date ranges (ISO codec) |
| Appointments create/edit | create key; appointment ID for edits | selections, notes, recurrence and dates (Dayjs codec) |
| Payments create/edit | create key; payment ID for edits | client, service, amount, quantity, date, description |
| Expenses create/edit | create key; expense ID for edits | category, description, amount, date |
| Services create/edit and price | create key; service ID for edits/prices | service fields and price |
| Reference-book creation | book-specific key | name |
| Course enrollment | client ID | course and initial-progress choice |
| Courses and course nodes | course ID and node identity | full authoring draft plus active node fields |
| Tasks | rule ID; custom-task create key | rule fields; recipient, message, due date |
| Users and invitations | user ID; invite create key | profile fields; invite email/role (never generated URL) |
| Current profile and availability | user ID | profile fields; working-hour/vacation date ranges |

Explicit exclusions:

- login, registration, password recovery/change, 2FA setup/verification,
  recovery codes, portal PIN, and generated invite/reset/calendar URLs are
  security-sensitive;
- destructive confirmations and task-delay input are short-lived confirmation
  actions;
- search, pagination, tabs, schedule position, and filters are transient or
  URL-owned;
- onboarding and one-click progress/status actions do not contain meaningful
  multi-field user input.

## Adding an eligible form

```tsx
const draftSchema = v.object({ title: v.optional(v.string()) });
const draft = useDurableForm({
  key: "draft:example:create",
  schema: draftSchema,
  form,
  codec: jsonDurableFormCodec<{ title?: string }>(),
  enabled: open,
});

<Form form={form} onValuesChange={draft.formProps.onValuesChange} />;
// After confirmed server success only:
await draft.clearAfterSuccess();
```

Checklist:

1. Put the key, schema, and non-JSON codec in the owning slice.
2. Use an entity-specific edit key and pass its baseline/version.
3. Show `status`, `restored`, stale recovery, and an explicit discard action.
4. Preserve the draft on close and on validation/request failure.
5. Call `clearAfterSuccess` only after the server confirms the mutation.
6. Never persist sensitive fields, generated tokens, URL state, or domain
   records.
7. Add storage/hydration/failure tests and run `npm run verify`.

## Browser verification

`npm run test:browser` exercises native IndexedDB recovery in headless Chromium,
including create-form remount, authenticated-user isolation, entity-scoped edit
restoration, and stale-baseline reapplication. It is part of `npm run verify`.
If Chromium is not installed system-wide, run `npx playwright install chromium`
or set `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` to an existing executable.
