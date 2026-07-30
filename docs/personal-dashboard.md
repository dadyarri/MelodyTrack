# Personal dashboard rules

`GET /dashboard?timezone=<IANA time zone>` describes only the authenticated
user's own work. Role does not change its provider scope: users, admins, and
superusers receive appointments whose provider is the authenticated user.

## Today and tomorrow

The endpoint materializes recurrence before reading the two-day window and
returns one object for today and one for tomorrow. Each object's `count` is
derived from its returned `appointments` collection rather than a separate
query.

An appointment is included when all of these rules hold:

- its start belongs to the requested local calendar day;
- it is assigned to the authenticated user;
- its status is `planned`;
- it is not deleted;
- it has not ended at the captured request time;
- the client is not on vacation on the appointment's local date.

Both UTC boundaries and displayed dates are derived from the same requested
timezone. Today is inclusive at local midnight; the following day is
exclusive. Appointments are ordered by start time and then client name.

## Personal summaries

`personalClientsCount` is the number of distinct clients with at least one
non-deleted appointment assigned to the authenticated user. Appointment status
does not erase that work relationship.

`monthIncome` sums completed and burned, non-deleted appointments assigned to
the authenticated user in the requested local month. Each appointment uses the
service price effective at its start; an appointment older than all known
prices uses the earliest known price.

Payments, debts, positive balances, expenses, and net profit are not treated as
personal values because their current records have no reliable provider
ownership.

## Organization summary

Admins and superusers additionally receive an `organization` object. Regular
users receive `null` and cannot read organization-wide values from this
endpoint.

The organization summary contains the former all-provider appointment counts,
total clients, debtors, total debt, positive balances, monthly income, monthly
expenses, and monthly net result. These values are deliberately separated from
the personal summaries so their scope is explicit.
