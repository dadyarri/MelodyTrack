# MelodyTrack.Init

Run database initialization before starting the backend:

```text
dotnet MelodyTrack.Init.dll --mode production
dotnet MelodyTrack.Init.dll --mode development
dotnet MelodyTrack.Init.dll --mode test
```

Generate a short-lived, one-time God Mode link without running database initialization:

```text
dotnet MelodyTrack.Init.dll god-mode
```

The command uses `GodMode__StateDirectory`, `GodMode__PublicBaseUrl`, and `GodMode__SessionSigningKey`. It stores only a SHA-256 token hash and writes the raw link only to standard output.

Configuration uses standard .NET keys, including `Database__ConnectionString`, `PublicUrl__BaseUrl`, the five purpose-separated `AuthenticationSecrets__*` keys, `PersonalData__CurrentKeyVersion`, and `PersonalData__CurrentKey`. Symmetric authentication keys use `base64:` followed by at least 32 random bytes. The JWT private key uses `base64:` followed by a P-256 PKCS#8 private key.

Password and portal PIN hashes use the versioned `mt-argon2id-v1` format: Argon2id v1.3, 64 MiB memory, three iterations, four lanes/threads, and a random 16-byte salt. Password and portal PIN verification use different peppers.

After the breaking authentication cutover, issue the first superuser reset URL only from a controlled server shell:

```shell
dotnet MelodyTrack.Init.dll --mode production --recover-superuser admin@example.com --show-recovery-url
```

The raw URL and a one-time recovery code are written only to standard output when `--show-recovery-url` is explicitly supplied. Enter the recovery code in the reset form when the superuser account requires 2FA. The URL expires after 30 minutes and supersedes earlier unused reset URLs for that account.

Development mode creates an idempotent representative data set and the deterministic non-production staff identity below. It preserves the original local generator's behavior within a rolling six-month window: 48 clients with complete contacts, acquisition sources, services with quarterly price history, provider-availability-aware appointments, realistic statuses, soft-deleted history, lesson notes, payments with at most two unpaid lessons per client, prepayments, categorized monthly expenses, and six weekly recurrence rules. Planned appointments extend 21 days beyond initialization. The seed uses up to four existing non-client staff members; on a fresh database every generated appointment is assigned to the deterministic identity.

```text
Email: dev.superuser@melodytrack.local
Password: MelodyTrack-Development-Only!
TOTP setup key: JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP
```

Add the TOTP setup key to an authenticator as a time-based token with issuer `MelodyTrack`, SHA-1, six digits, and a 30-second period. These credentials are development-only and are never configured as production defaults. Production mode preserves bootstrap-invite behavior and logs only an opaque invite reference unless `Initialization__LogBootstrapSecrets=true` (or the legacy recovery environment variable) is set deliberately.
