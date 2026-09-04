# Goal

Reset the requested local-development business data and rewrite the local seed script so generated lessons follow each teacher’s availability, use one-hour whole-hour slots without per-teacher collisions, produce at most two unpaid lessons per client, and contain natural Bogus-generated identities with no “Демо ·” naming.

# Approach

Treat the database reset and the script rewrite as two separate operations: first perform a guarded, transactional cleanup against the launch-profile database after verifying that its host is local, then refactor the single-file seed script around availability-derived candidate slots and client-level payment reconciliation. Reuse the backend’s established availability and timezone rules from [UserAvailabilityService.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/MelodyTrack.Backend/Services/UserAvailabilityService.cs?type=file&root=%252F) and UTC conversion helper from [DateTimeUtils.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/MelodyTrack.Backend/Utils/DateTimeUtils.cs?type=file&root=%252F), rather than duplicating a conflicting definition of working time. Because no timezone preference was returned, add a `--timezone` option whose default is `TimeZoneInfo.Local.Id`; because no lookup-cleanup preference was returned, delete only old lookup rows whose names begin with “Демо ·”, preserving unrelated client sources and expense categories.

# File Changes

- **Modify** [SeedLocalData.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/scripts/SeedLocalData.cs?type=file&root=%252F) (current lines 17–667): remove marker/prefix-based demo behavior and handcrafted patronymics; add timezone parsing; load teacher availability; generate collision-free whole-hour slots; make every appointment exactly one hour; reconcile payments per client so no balance represents more than two unpaid billable appointments; align recurring-rule starts with valid free teacher slots; and update output/help text.
- **No schema or migration files:** the entities already expose the needed sets in [AppDbContext.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/MelodyTrack.Backend/Data/AppDbContext.cs?type=file&root=%252F), and existing foreign keys already cascade or null dependent references appropriately.

# Implementation Steps

## Task 1: Safely clear the local development datasets

1. Read the launch-profile connection string used by [SeedLocalData.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/scripts/SeedLocalData.cs?type=file&root=%252F) (current lines 21–40) without printing credentials, parse it with `NpgsqlConnectionStringBuilder`, and abort unless the host is one of the same local hosts accepted by `EnsureLocalDatabase`.
2. In one explicit database transaction, delete rows from `Payments`, `Expenses`, and `Appointments`, then `Clients` and `Services`. Let mapped foreign keys clean up recurrence rules, service price history, client contacts/vacations/enrollments/subscriptions, and null retained task/user references according to [AppDbContext.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/MelodyTrack.Backend/Data/AppDbContext.cs?type=file&root=%252F) (current relationship configuration around lines 248–485).
3. In that transaction, remove only legacy `ClientSources` and `ExpenseCategories` whose names start with `Демо ·`; do not clear unrelated lookup rows. Commit only after all statements succeed.
4. Run read-only counts after the commit and report that `Appointments`, `Clients`, `Payments`, `Expenses`, and `Services` are zero; also report how many legacy prefixed lookup rows were removed.

## Task 2: Replace marker-driven seed setup with clean, reusable seed data

1. In [SeedLocalData.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/scripts/SeedLocalData.cs?type=file&root=%252F) (current lines 17 and 55–59), remove `demoMarker` and the source-name idempotency shortcut. Replace it with a precondition that refuses to seed when any of the five primary target tables already contains rows, preventing an accidental duplicate dataset without relying on visible name prefixes.
2. Update source, service, expense-category, expense-description, contact, console, and help strings in [SeedLocalData.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/scripts/SeedLocalData.cs?type=file&root=%252F) (current lines 125–195, 235–240, 378–383, and 643–654) so neither `Демо ·` nor `demo` is emitted. Resolve cleanly named sources/categories from existing rows first and create only missing definitions, so unrelated lookup data remains usable and reruns do not create clean-name duplicates.
3. In client generation (current lines 199–245), use Bogus’s Russian `Person`/name dataset as the sole source of first and last names, remove the handwritten patronymic array, and leave `Patronymic` unset unless Bogus natively supplies it. Use Bogus internet/phone data for contacts instead of identity strings containing a demo prefix, while keeping generated values compatible with the model annotations in [ClientContacts.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/MelodyTrack.Backend/Data/Models/ClientContacts.cs?type=file&root=%252F).

## Task 3: Generate valid teacher slots

1. Extend `SeedOptions` in [SeedLocalData.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/scripts/SeedLocalData.cs?type=file&root=%252F) (current lines 601–667) with `--timezone <IANA-or-system-id>`, default it to `TimeZoneInfo.Local.Id`, validate it with `TimeZoneInfo.FindSystemTimeZoneById`, and document the effective timezone in help and completion output.
2. After selecting non-client providers (current lines 61–73), load their availability snapshots through `UserAvailabilityService.GetAvailabilitiesAsync`. This preserves configured working days, minute boundaries, vacations, and the service’s Monday–Friday 10:00–20:00 fallback when a teacher has no stored schedule, as defined in [UserAvailabilityService.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/MelodyTrack.Backend/Services/UserAvailabilityService.cs?type=file&root=%252F) (current lines 68–127 and 170–181).
3. Replace the current day-level 09:00/90-minute/random-quarter-hour algorithm (current lines 267–310) with candidate generation per teacher and local calendar date: skip non-working days and vacations; round the configured start upward to the next `:00`; enumerate one-hour slots while `slotStart + 60 minutes <= EndMinuteOfDay`; convert each local slot to UTC using [DateTimeUtils.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/MelodyTrack.Backend/Utils/DateTimeUtils.cs?type=file&root=%252F).
4. Randomly select from those unique `(provider, local date, start hour)` candidates to retain varied data density. Set every `EndDate` to `StartDate.AddHours(1)`, including consultations, and track occupied keys so a teacher can never receive overlapping generated appointments; allow equal UTC/local slots for different teachers.
5. Build current-year recurrence rules (current lines 420–447) from the same future free-slot pool. Ensure each rule’s start is on a whole local hour, is inside its provider’s working hours and outside vacations, and does not collide with an already generated appointment/rule for that provider.

## Task 4: Bound client debt by billable appointments

1. Rework `CreatePayments` in [SeedLocalData.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/scripts/SeedLocalData.cs?type=file&root=%252F) (current lines 315–370) around the same billable set used by the application balance calculation: non-deleted past appointments with `Completed` or `Burned` status, priced at the appointment date. This matches [ClientWithBalanceDto.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/MelodyTrack.Backend/Api/Clients/Responses/ClientWithBalanceDto.cs?type=file&root=%252F) (current lines 32–70).
2. Group billable appointments by client, choose a deterministic seeded outstanding count of 0–2, and create payments totaling every other appointment’s price. Do not use the existing independent 22% skip plus 12% half-payment probabilities, which can accumulate unbounded debt.
3. Keep any generated prepayment separate and ensure it can only reduce the outstanding balance. After payment creation, calculate each client’s outstanding ledgers using oldest-payment-first allocation (the same behavior used by debtor-task candidates) and throw before `SaveChangesAsync` if any client has more than two ledgers with a positive remainder.
4. Add pre-save invariants in the script for slot duration, local-minute alignment, provider availability, provider overlap, and debt count. This makes a bad generated dataset roll back with a specific error rather than being partially committed.

# Acceptance Criteria

- The configured local database host passes the existing local-host allowlist before destructive SQL runs; a non-local host is rejected before any delete.
- After Task 1, read-only counts for `Appointments`, `Clients`, `Payments`, `Expenses`, and `Services` are each exactly 0.
- Rows in `Users`, `Roles`, `RecurrenceTypes`, course definitions, working hours, and user vacations are not deleted by the reset.
- Legacy sources/categories beginning with `Демо ·` are removed, while lookup rows with other names remain unchanged.
- Running the rewritten script against the cleared database produces at least one client, service, appointment, payment, and expense and completes in a single committed transaction.
- For every seeded appointment, `EndDate - StartDate == 01:00:00`.
- Converting every seeded appointment to the selected seed timezone yields `StartDate.Minute == 0` and `StartDate.Second == 0`.
- Every seeded appointment and recurrence-rule start falls on a working day, outside teacher vacations, and wholly within that teacher’s configured/fallback start and end minutes.
- For any one provider, no two non-deleted seeded appointments have intervals satisfying `left.StartDate < right.EndDate && right.StartDate < left.EndDate`; identical slots across different providers are allowed.
- For every seeded client, oldest-first allocation of all payments against completed/burned, non-deleted appointment charges leaves positive remainder on no more than two appointments.
- No source, category, service, contact, console message, or help text generated by the script contains `Демо ·`, a `demo` identity prefix, or equivalent marker.
- Client first/last names come directly from Bogus’s Russian person/name dataset; the script contains no hardcoded name or patronymic list.
- A second seed attempt while any target dataset is populated exits without inserting additional rows.

# Verification Steps

Per the repository instruction, do not run builds, tests, formatters, or verification pipelines unless the user explicitly authorizes verification after the change batch.

1. Static review: inspect [SeedLocalData.cs](air-file://gmleliomfc4nek1lbelp/home/dadyarri/Projects/MelodyTrack/MelodyTrack.Backend/scripts/SeedLocalData.cs?type=file&root=%252F) for the local-host guard, empty-target precondition, availability-derived slot generation, one-hour duration, debt invariant, and absence of `Демо`/`demo` literals.
2. Database reset check: issue read-only `COUNT(*)` queries for the five requested tables immediately after cleanup; also compare retained user/role/working-hours counts before and after.
3. If script execution is authorized, run `dotnet run scripts/SeedLocalData.cs -- --year 2026 --clients 60 --seed 42 --timezone Europe/Moscow` from the backend repository, then run read-only SQL assertions for duration, local hour alignment, overlap pairs, availability/vacation violations, target row counts, and maximum outstanding debt ledgers.
4. If build verification is separately authorized, run `DOTNET_CLI_HOME=/tmp/dotnet-home dotnet build MelodyTrack.slnx -v:minimal` from the backend repository. No automated test file is proposed because the changed artifact is a standalone data-mutating script; its deterministic seed plus pre-save/database assertions directly verify the relevant invariants.
5. Edge cases: seed with one teacher, a teacher with no stored hours (fallback), a teacher with split-off/non-working weekdays, a vacation spanning candidate dates, working-hour bounds not on whole hours (for example 09:30–17:30), and a year whose available period contains fewer slots than the requested client population.

# Risks & Mitigations

- **Destructive reset targets the wrong database:** retain the launch-profile source, parse the connection string, enforce the existing localhost allowlist, print only host/database (never credentials), and wrap all deletes in one transaction.
- **Cascade cleanup removes dependent local client data beyond the five named tables:** this is required by the current model’s referential integrity; report cascaded entity categories before execution and preserve user/reference configuration. Retained task/audit records may keep history with nullable client/appointment links by design.
- **Timezone/DST conversion creates invalid or ambiguous wall-clock slots:** validate the timezone and use `TimeZoneInfo`/the existing UTC helper; skip invalid local times and choose a consistent offset for ambiguous times before applying the local-minute invariant.
- **Sparse teacher availability cannot satisfy the prior daily density:** generate only from actual capacity and allow fewer appointments rather than violating hours or creating overlaps; fail clearly only when no valid slot exists at all.
- **Clean source/category names collide with existing local lookup rows:** resolve by exact clean name and reuse matches instead of blindly inserting duplicates.
