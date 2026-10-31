# Исходная схема production для переноса

Срез 31.10.2026. Референс: `1636c2c4dabdd33420209cfd6617cc3c82fe2433`, PostgreSQL 17, все 49 EF-миграций до `20260904141137_Stage14TimedVacations` включительно. По D-416 эта версия принята пользователем как production-схема. Это инвентаризация исходного кода; подключения к БД, запуска Init, миграций, сборки или тестов не было. Количества строк и наличие конкретных состояний в production здесь не утверждаются.

Связанные документы: [основания переноса](data-migration.md), [карта соответствий](migration-map.md), [открытые вопросы](open-questions.md).

## Источники и состав

Все пути исходников в этом документе относятся к указанному commit в референсном worktree, а не к текущему HEAD основного репозитория.

| Источник | Что извлечено |
| --- | --- |
| `MelodyTrack.Data/Migrations/AppDbContextModelSnapshot.cs` | Таблицы, поля, типы, обязательность, PK, индексы, FK и CHECK |
| `MelodyTrack.Data/Migrations/*.cs`, без designer | История и преобразования в `Up`, включая DML |
| `MelodyTrack.Data/AppDbContext.cs`, `MelodyTrack.Core/Models`, `MelodyTrack.Core/Enums` | Конвертеры, семантика значений, seed справочников |
| `MelodyTrack.Init/quartz.sql`, `MelodyTrack.Data/Initialization/DatabaseInitializationService.cs` | Внешние к EF таблицы Quartz и порядок инициализации |
| Сервисы расписания, задач, курсов и расчёта баланса | Значения, которые нельзя вывести из DDL |

- В EF snapshot: **45 таблиц, 327 столбцов, 61 FK, 76 индексов** (из них 21 уникальный), **7 CHECK**. PK считаются отдельно от индексов.
- В `quartz.sql`: **11 таблиц** и 11 явно создаваемых индексов, отдельные составные PK/FK.
- `__EFMigrationsHistory`: ещё одна служебная таблица. Итого ожидается **57 таблиц** от перечисленных механизмов; это состав исходников, не замер каталога production.
- Все 45 EF-таблиц имеют PK `Id: bytea`; ULID кодируется в 16 байт через `UlidToBytesConverter`. Ограничение длины `bytea` в snapshot не задано.
- Даты-моменты имеют тип `timestamp with time zone`; `date` остаётся для дней рождения и `BusinessDate`. Денежные значения — `numeric` без фиксированной точности/масштаба.
- Расширение модели: `fuzzystrmatch`. Представлений, пользовательских SQL-функций и триггеров в snapshot и SQL-вставках миграций не объявлено.
- SHA-256 snapshot: `b254d0a513a35efa52a3defb58e0bf42ab16dc2c1129443376f2c7104baf6880`.
- SHA-256 `quartz.sql`: `afece8c00d54ac006fbaad98f77060649ea952867b685576e7999497d84e185c`.

## Каталог таблиц EF

`NULL = да` означает допустимый NULL в snapshot. Для каждого поля показан физический тип; строковые лимиты входят в тип. «Поведение удаления не задано» означает отсутствие явного `OnDelete` в snapshot; каскад или `SET NULL` не домысливаются. Служебные ссылки без FK перечислены после каталога.

### Appointments

Сущность `Appointment`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `ClientId` | `bytea` | нет | — |
| `CourseThemeId` | `bytea` | да | — |
| `EndDate` | `timestamp with time zone` | нет | — |
| `IsDeleted` | `boolean` | нет | — |
| `LessonNotes` | `character varying(4000)` | да | — |
| `ProviderId` | `bytea` | да | — |
| `RecurringRuleId` | `bytea` | да | — |
| `ServiceId` | `bytea` | нет | — |
| `StartDate` | `timestamp with time zone` | нет | — |
| `Status` | `integer` | нет | — |

Индексы:

- (`ClientId`).
- (`CourseThemeId`).
- (`ProviderId`).
- (`RecurringRuleId`).
- (`ServiceId`).

Внешние ключи:

- `ClientId` → `Clients.Id`; ON DELETE Cascade.
- `CourseThemeId` → `CourseThemes.Id`; ON DELETE SetNull.
- `ProviderId` → `Users.Id`; поведение удаления не задано.
- `RecurringRuleId` → `RecurrenceRules.Id`; поведение удаления не задано.
- `ServiceId` → `Services.Id`; ON DELETE Cascade.

### AuditLogs

Сущность `AuditLog`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `Action` | `character varying(100)` | нет | — |
| `ActorDisplayName` | `character varying(200)` | да | — |
| `ActorEmail` | `character varying(200)` | да | — |
| `ActorUserId` | `bytea` | да | — |
| `Category` | `character varying(50)` | нет | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `Details` | `character varying(1000)` | да | — |
| `EntityId` | `character varying(64)` | да | — |
| `EntityType` | `character varying(100)` | нет | — |
| `SourceIpAddress` | `character varying(45)` | да | — |

### CalendarSubscriptions

Сущность `CalendarSubscription`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `ClientId` | `bytea` | да | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `RevokedAtUtc` | `timestamp with time zone` | да | — |
| `Token` | `character varying(128)` | нет | — |
| `UserId` | `bytea` | да | — |

Индексы:

- (`ClientId`).
- UNIQUE (`Token`).
- (`UserId`).

Внешние ключи:

- `ClientId` → `Clients.Id`; ON DELETE Cascade.
- `UserId` → `Users.Id`; ON DELETE Cascade.

### ClientContacts

Сущность `ClientContacts`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `Email` | `text` | да | — |
| `Phone` | `text` | да | — |
| `Telegram` | `text` | да | — |
| `Vk` | `text` | да | — |

### ClientPortalLoginLinks

Сущность `ClientPortalLoginLink`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `FailedPinAttempts` | `integer` | нет | — |
| `LastFailedPinAttemptAtUtc` | `timestamp with time zone` | да | — |
| `PinHash` | `text` | да | — |
| `PinSetAtUtc` | `timestamp with time zone` | да | — |
| `RevokedAtUtc` | `timestamp with time zone` | да | — |
| `TokenHash` | `text` | да | — |
| `UserId` | `bytea` | нет | — |

Индексы:

- UNIQUE (`TokenHash`).
- (`UserId`).

Внешние ключи:

- `UserId` → `Users.Id`; ON DELETE Cascade.

### ClientPortalSavedIdentityReferences

Сущность `ClientPortalSavedIdentityReference`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `LastUsedAtUtc` | `timestamp with time zone` | нет | — |
| `LoginLinkId` | `bytea` | нет | — |
| `ReferenceHash` | `text` | нет | — |

Индексы:

- (`LoginLinkId`).
- UNIQUE (`ReferenceHash`).

Внешние ключи:

- `LoginLinkId` → `ClientPortalLoginLinks.Id`; ON DELETE Cascade.

### ClientSources

Сущность `ClientSource`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `Name` | `character varying(200)` | нет | — |

### ClientVacations

Сущность `ClientVacation`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `ClientId` | `bytea` | нет | — |
| `EndDate` | `timestamp with time zone` | нет | — |
| `StartDate` | `timestamp with time zone` | нет | — |

Индексы:

- (`ClientId`).

Внешние ключи:

- `ClientId` → `Clients.Id`; ON DELETE Cascade.

### Clients

Сущность `Client`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `ContactsId` | `bytea` | нет | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `DateOfBirth` | `date` | да | — |
| `FirstName` | `character varying(100)` | нет | — |
| `IsLeadClosed` | `boolean` | нет | — |
| `LastName` | `character varying(100)` | нет | — |
| `Patronymic` | `character varying(100)` | да | — |
| `SourceId` | `bytea` | да | — |

Индексы:

- (`ContactsId`).
- (`SourceId`).

Внешние ключи:

- `ContactsId` → `ClientContacts.Id`; ON DELETE Cascade.
- `SourceId` → `ClientSources.Id`; ON DELETE SetNull.

### CourseBlocks

Сущность `CourseBlock`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `CourseId` | `bytea` | нет | — |
| `Description` | `character varying(2000)` | да | — |
| `Order` | `integer` | нет | — |
| `Title` | `character varying(200)` | нет | — |

Индексы:

- UNIQUE (`CourseId`, `Order`).

Внешние ключи:

- `CourseId` → `Courses.Id`; ON DELETE Cascade.

### CourseBranches

Сущность `CourseBranch`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `BlockId` | `bytea` | нет | — |
| `Description` | `character varying(2000)` | да | — |
| `Order` | `integer` | нет | — |
| `Title` | `character varying(200)` | нет | — |

Индексы:

- UNIQUE (`BlockId`, `Order`).

Внешние ключи:

- `BlockId` → `CourseBlocks.Id`; ON DELETE Cascade.

### CourseEnrollmentThemes

Сущность `CourseEnrollmentTheme`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `CompletedAtUtc` | `timestamp with time zone` | да | — |
| `CourseThemeId` | `bytea` | нет | — |
| `EnrollmentId` | `bytea` | нет | — |
| `StartedAtUtc` | `timestamp with time zone` | да | — |
| `State` | `integer` | нет | — |
| `UnlockedAtUtc` | `timestamp with time zone` | да | — |
| `WaitingForHomeworkAtUtc` | `timestamp with time zone` | да | — |

Индексы:

- (`CourseThemeId`).
- UNIQUE (`EnrollmentId`, `CourseThemeId`).

Внешние ключи:

- `CourseThemeId` → `CourseThemes.Id`; ON DELETE Cascade.
- `EnrollmentId` → `CourseEnrollments.Id`; ON DELETE Cascade.

### CourseEnrollments

Сущность `CourseEnrollment`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `ClientId` | `bytea` | нет | — |
| `CourseId` | `bytea` | нет | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `UpdatedAtUtc` | `timestamp with time zone` | нет | — |

Индексы:

- (`CourseId`).
- UNIQUE (`ClientId`, `CourseId`).

Внешние ключи:

- `ClientId` → `Clients.Id`; ON DELETE Cascade.
- `CourseId` → `Courses.Id`; ON DELETE Cascade.

### CourseLevels

Сущность `CourseLevel`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `CourseId` | `bytea` | нет | — |
| `Order` | `integer` | нет | — |
| `RequiredExperiencePoints` | `integer` | нет | — |
| `Title` | `character varying(200)` | нет | — |

Индексы:

- UNIQUE (`CourseId`, `Order`).

Внешние ключи:

- `CourseId` → `Courses.Id`; ON DELETE Cascade.

### CourseThemeDependencies

Сущность `CourseThemeDependency`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `DependsOnThemeId` | `bytea` | нет | — |
| `ThemeId` | `bytea` | нет | — |

Индексы:

- (`DependsOnThemeId`).
- UNIQUE (`ThemeId`, `DependsOnThemeId`).

Внешние ключи:

- `DependsOnThemeId` → `CourseThemes.Id`; ON DELETE Restrict.
- `ThemeId` → `CourseThemes.Id`; ON DELETE Cascade.

### CourseThemes

Сущность `CourseTheme`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `BranchId` | `bytea` | нет | — |
| `Description` | `character varying(4000)` | да | — |
| `ExperiencePointsReward` | `integer` | нет | — |
| `HomeworkContent` | `text` | да | — |
| `Key` | `character varying(100)` | нет | — |
| `LessonContent` | `text` | да | — |
| `Order` | `integer` | нет | — |
| `Title` | `character varying(200)` | нет | — |

Индексы:

- UNIQUE (`BranchId`, `Order`).

Внешние ключи:

- `BranchId` → `CourseBranches.Id`; ON DELETE Cascade.

### Courses

Сущность `Course`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `Description` | `character varying(2000)` | да | — |
| `Name` | `character varying(200)` | нет | — |
| `UpdatedAtUtc` | `timestamp with time zone` | нет | — |

Индексы:

- (`Name`).

### CustomTasks

Сущность `CustomTask`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `CancelledAtUtc` | `timestamp with time zone` | да | — |
| `CancelledByUserId` | `bytea` | да | — |
| `ClientId` | `bytea` | да | — |
| `CompletedAtUtc` | `timestamp with time zone` | да | — |
| `CompletedByUserId` | `bytea` | да | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `CreatedByUserId` | `bytea` | да | — |
| `DelayedAtUtc` | `timestamp with time zone` | да | — |
| `DelayedByUserId` | `bytea` | да | — |
| `DelayedUntilUtc` | `timestamp with time zone` | да | — |
| `DueAtUtc` | `timestamp with time zone` | нет | — |
| `MessageText` | `character varying(2000)` | нет | — |
| `Phone` | `character varying(100)` | да | — |
| `RecipientName` | `character varying(200)` | нет | — |
| `Telegram` | `character varying(200)` | да | — |
| `Title` | `character varying(200)` | нет | — |
| `Vk` | `character varying(200)` | да | — |

Индексы:

- (`CancelledByUserId`).
- (`CompletedByUserId`).
- (`CreatedByUserId`).
- (`DelayedByUserId`).
- (`DelayedUntilUtc`).
- (`ClientId`, `DueAtUtc`).

Внешние ключи:

- `CancelledByUserId` → `Users.Id`; ON DELETE SetNull.
- `ClientId` → `Clients.Id`; ON DELETE SetNull.
- `CompletedByUserId` → `Users.Id`; ON DELETE SetNull.
- `CreatedByUserId` → `Users.Id`; ON DELETE SetNull.
- `DelayedByUserId` → `Users.Id`; ON DELETE SetNull.

### ExpenseCategories

Сущность `ExpenseCategory`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `Name` | `character varying(200)` | нет | — |

### Expenses

Сущность `Expense`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `Amount` | `numeric` | нет | — |
| `CategoryId` | `bytea` | да | — |
| `Date` | `timestamp with time zone` | нет | — |
| `Description` | `character varying(200)` | нет | — |

Индексы:

- (`CategoryId`).

Внешние ключи:

- `CategoryId` → `ExpenseCategories.Id`; ON DELETE SetNull.

### InviteCodes

Сущность `InviteCode`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `Code` | `bytea` | нет | — |
| `Email` | `text` | да | — |
| `RoleId` | `bytea` | нет | — |
| `ValidUntil` | `timestamp with time zone` | нет | — |
| `WasUsed` | `boolean` | нет | — |

Индексы:

- (`RoleId`).

Внешние ключи:

- `RoleId` → `Roles.Id`; ON DELETE Cascade.

### NotificationPushDeliveries

Сущность `NotificationPushDelivery`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `AttemptCount` | `integer` | нет | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `DeliveredAtUtc` | `timestamp with time zone` | да | — |
| `FailedAtUtc` | `timestamp with time zone` | да | — |
| `LastFailureCode` | `character varying(64)` | да | — |
| `NextAttemptAtUtc` | `timestamp with time zone` | нет | — |
| `NotificationId` | `bytea` | нет | — |
| `PushSubscriptionId` | `bytea` | нет | — |
| `Status` | `integer` | нет | — |

Индексы:

- (`PushSubscriptionId`).
- UNIQUE (`NotificationId`, `PushSubscriptionId`).
- (`Status`, `NextAttemptAtUtc`).

Внешние ключи:

- `NotificationId` → `Notifications.Id`; ON DELETE Cascade.
- `PushSubscriptionId` → `PushSubscriptions.Id`; ON DELETE Cascade.

### Notifications

Сущность `Notification`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `ClientId` | `bytea` | да | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `DeepLink` | `character varying(1000)` | да | — |
| `ExpiresAtUtc` | `timestamp with time zone` | да | — |
| `PushMessage` | `character varying(200)` | нет | — |
| `ReadAtUtc` | `timestamp with time zone` | да | — |
| `ReferenceId` | `bytea` | да | — |
| `ReferenceType` | `character varying(100)` | да | — |
| `Summary` | `character varying(1000)` | нет | — |
| `Title` | `character varying(200)` | нет | — |
| `Type` | `character varying(100)` | нет | — |
| `UserId` | `bytea` | да | — |

Индексы:

- (`ClientId`, `CreatedAtUtc`).
- (`UserId`, `CreatedAtUtc`).

Внешние ключи:

- `ClientId` → `Clients.Id`; ON DELETE Cascade.
- `UserId` → `Users.Id`; ON DELETE Cascade.

Ограничения:

- `CK_Notifications_ExactlyOneRecipient`: `("UserId" IS NOT NULL AND "ClientId" IS NULL) OR ("UserId" IS NULL AND "ClientId" IS NOT NULL)`.

### PasswordRestorationRequests

Сущность `PasswordRestorationRequest`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `Email` | `text` | нет | — |
| `Token` | `text` | нет | — |
| `ValidUntil` | `timestamp with time zone` | нет | — |
| `WasUsed` | `boolean` | нет | — |

### Payments

Сущность `Payment`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `Amount` | `numeric` | нет | — |
| `ClientId` | `bytea` | нет | — |
| `Date` | `timestamp with time zone` | нет | — |
| `Description` | `character varying(200)` | нет | — |
| `ServiceId` | `bytea` | да | — |

Индексы:

- (`ClientId`).
- (`ServiceId`).

Внешние ключи:

- `ClientId` → `Clients.Id`; ON DELETE Cascade.
- `ServiceId` → `Services.Id`; поведение удаления не задано.

### PushSubscriptions

Сущность `PushSubscription`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `Auth` | `character varying(512)` | нет | — |
| `ClientId` | `bytea` | да | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `Endpoint` | `character varying(2048)` | нет | — |
| `ExpiresAtUtc` | `timestamp with time zone` | да | — |
| `P256Dh` | `character varying(512)` | нет | — |
| `SessionId` | `bytea` | нет | — |
| `UpdatedAtUtc` | `timestamp with time zone` | нет | — |
| `UserId` | `bytea` | да | — |

Индексы:

- (`ClientId`).
- UNIQUE (`Endpoint`).
- (`SessionId`).
- (`UserId`).

Внешние ключи:

- `ClientId` → `Clients.Id`; ON DELETE Cascade.
- `SessionId` → `Sessions.Id`; ON DELETE Cascade.
- `UserId` → `Users.Id`; ON DELETE Cascade.

Ограничения:

- `CK_PushSubscriptions_ExactlyOnePrincipal`: `("UserId" IS NOT NULL AND "ClientId" IS NULL) OR ("UserId" IS NULL AND "ClientId" IS NOT NULL)`.

### RecoveryCodes

Сущность `RecoveryCode`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `Code` | `text` | нет | — |
| `UserId` | `bytea` | нет | — |
| `WasUsed` | `boolean` | нет | — |

Индексы:

- (`UserId`).

Внешние ключи:

- `UserId` → `Users.Id`; ON DELETE Cascade.

### RecurrenceRules

Сущность `AppointmentRecurrenceRule`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `ClientId` | `bytea` | нет | — |
| `EndDate` | `timestamp with time zone` | да | — |
| `ProviderId` | `bytea` | да | — |
| `RecurrencePattern` | `integer` | да | — |
| `RecurrenceTypeId` | `bytea` | нет | — |
| `ServiceId` | `bytea` | нет | — |
| `StartDate` | `timestamp with time zone` | нет | — |

Индексы:

- (`ClientId`).
- (`ProviderId`).
- (`RecurrenceTypeId`).
- (`ServiceId`).

Внешние ключи:

- `ClientId` → `Clients.Id`; ON DELETE Cascade.
- `ProviderId` → `Users.Id`; поведение удаления не задано.
- `RecurrenceTypeId` → `RecurrenceTypes.Id`; ON DELETE Cascade.
- `ServiceId` → `Services.Id`; ON DELETE Cascade.

### RecurrenceTypes

Сущность `RecurrenceType`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `DisplayName` | `text` | нет | — |
| `Type` | `integer` | нет | — |

### RecurringTaskExecutions

Сущность `RecurringTaskExecution`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `AppointmentId` | `bytea` | да | — |
| `BusinessDate` | `date` | нет | — |
| `CancelledAtUtc` | `timestamp with time zone` | да | — |
| `CancelledByUserId` | `bytea` | да | — |
| `ClientId` | `bytea` | да | — |
| `CompletedAtUtc` | `timestamp with time zone` | да | — |
| `CompletedByUserId` | `bytea` | да | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `DeduplicationKey` | `character varying(500)` | нет | — |
| `DelayedAtUtc` | `timestamp with time zone` | да | — |
| `DelayedByUserId` | `bytea` | да | — |
| `DelayedUntilUtc` | `timestamp with time zone` | да | — |
| `GeneratedText` | `character varying(2000)` | да | — |
| `RecipientType` | `integer` | нет | — |
| `RuleId` | `bytea` | нет | — |
| `Status` | `integer` | нет | — |
| `TeacherId` | `bytea` | да | — |

Индексы:

- (`AppointmentId`).
- (`CancelledByUserId`).
- (`CompletedByUserId`).
- UNIQUE (`DeduplicationKey`).
- (`DelayedByUserId`).
- (`TeacherId`).
- (`RuleId`, `Status`).
- (`ClientId`, `RuleId`, `CompletedAtUtc`).

Внешние ключи:

- `AppointmentId` → `Appointments.Id`; ON DELETE SetNull.
- `CancelledByUserId` → `Users.Id`; ON DELETE SetNull.
- `ClientId` → `Clients.Id`; ON DELETE SetNull.
- `CompletedByUserId` → `Users.Id`; ON DELETE SetNull.
- `DelayedByUserId` → `Users.Id`; ON DELETE SetNull.
- `RuleId` → `RecurringTaskRules.Id`; ON DELETE Cascade.
- `TeacherId` → `Users.Id`; ON DELETE SetNull.

### RecurringTaskRules

Сущность `RecurringTaskRule`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `CooldownDays` | `integer` | да | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `IsEnabled` | `boolean` | нет | — |
| `MessageTemplate` | `character varying(1000)` | нет | — |
| `Name` | `character varying(200)` | нет | — |
| `OffsetMinutes` | `integer` | да | — |
| `Type` | `integer` | нет | — |
| `UpdatedAtUtc` | `timestamp with time zone` | нет | — |

### RequestReplays

Сущность `RequestReplay`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `CallerId` | `bytea` | нет | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `Endpoint` | `character varying(100)` | нет | — |
| `ReplayKey` | `character varying(100)` | нет | — |
| `RequestFingerprint` | `character varying(64)` | нет | — |
| `ResponseEntityId` | `bytea` | да | — |

Индексы:

- UNIQUE (`Endpoint`, `CallerId`, `ReplayKey`).

### Roles

Сущность `Role`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `DisplayName` | `text` | нет | — |
| `RoleName` | `integer` | нет | — |

### ServicePriceHistory

Сущность `ServicePrice`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `EffectiveDate` | `timestamp with time zone` | нет | — |
| `Price` | `numeric` | нет | — |
| `ServiceId` | `bytea` | нет | — |

Индексы:

- (`ServiceId`).

Внешние ключи:

- `ServiceId` → `Services.Id`; ON DELETE Cascade.

### Services

Сущность `Service`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `Description` | `character varying(200)` | да | — |
| `IsConsultation` | `boolean` | нет | — |
| `Name` | `character varying(200)` | нет | — |
| `PublicName` | `character varying(200)` | да | — |

### Sessions

Сущность `Session`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `DeviceInfo` | `text` | нет | — |
| `RefreshToken` | `text` | нет | — |
| `UserId` | `bytea` | нет | — |
| `ValidUntil` | `timestamp with time zone` | нет | — |
| `WasRevoked` | `boolean` | нет | — |

Индексы:

- (`UserId`).

Внешние ключи:

- `UserId` → `Users.Id`; ON DELETE Cascade.

### SystemNoticeRecipients

Сущность `SystemNoticeRecipient`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `ClientId` | `bytea` | да | — |
| `DismissedAtUtc` | `timestamp with time zone` | да | — |
| `NoticeId` | `bytea` | нет | — |
| `ReadAtUtc` | `timestamp with time zone` | да | — |
| `UserId` | `bytea` | да | — |

Индексы:

- (`ClientId`).
- (`UserId`).
- UNIQUE (`NoticeId`, `ClientId`); WHERE `"ClientId" IS NOT NULL`.
- UNIQUE (`NoticeId`, `UserId`); WHERE `"UserId" IS NOT NULL`.

Внешние ключи:

- `ClientId` → `Clients.Id`; ON DELETE Cascade.
- `NoticeId` → `SystemNotices.Id`; ON DELETE Cascade.
- `UserId` → `Users.Id`; ON DELETE Cascade.

Ограничения:

- `CK_SystemNoticeRecipients_ExactlyOneRecipient`: `("UserId" IS NOT NULL AND "ClientId" IS NULL) OR ("UserId" IS NULL AND "ClientId" IS NOT NULL)`.

### SystemNotices

Сущность `SystemNotice`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `AudienceType` | `integer` | нет | — |
| `Body` | `character varying(4000)` | нет | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `Dismissible` | `boolean` | нет | — |
| `ExpiresAtUtc` | `timestamp with time zone` | да | — |
| `Severity` | `integer` | нет | — |
| `ShowBeforeAuthentication` | `boolean` | нет | — |
| `Title` | `character varying(200)` | нет | — |
| `UpdatedAtUtc` | `timestamp with time zone` | да | — |

### UserOnboardingStates

Сущность `UserOnboardingState`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `CompletedAtUtc` | `timestamp with time zone` | да | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `CurrentPath` | `text` | нет | — |
| `CurrentStep` | `text` | нет | — |
| `DefinitionVersion` | `integer` | нет | DEFAULT `1`; ValueGeneratedOnAdd |
| `Status` | `integer` | нет | — |
| `UpdatedAtUtc` | `timestamp with time zone` | нет | — |
| `UserId` | `bytea` | нет | — |

Индексы:

- UNIQUE (`UserId`).

Внешние ключи:

- `UserId` → `Users.Id`; ON DELETE Cascade.

### UserVacations

Сущность `UserVacation`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `EndDate` | `timestamp with time zone` | нет | — |
| `StartDate` | `timestamp with time zone` | нет | — |
| `UserId` | `bytea` | нет | — |

Индексы:

- (`UserId`).

Внешние ключи:

- `UserId` → `Users.Id`; ON DELETE Cascade.

### UserWorkingHoursDays

Сущность `UserWorkingHoursDay`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `DayOfWeek` | `integer` | нет | — |
| `EndMinuteOfDay` | `integer` | нет | — |
| `IsWorkingDay` | `boolean` | нет | — |
| `StartMinuteOfDay` | `integer` | нет | — |
| `UserId` | `bytea` | нет | — |

Индексы:

- UNIQUE (`UserId`, `DayOfWeek`).

Внешние ключи:

- `UserId` → `Users.Id`; ON DELETE Cascade.

### Users

Сущность `User`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `ClientId` | `bytea` | да | — |
| `Email` | `text` | нет | — |
| `EmailBlindIndex` | `text` | нет | — |
| `FirstName` | `text` | нет | — |
| `LastName` | `text` | нет | — |
| `Password` | `text` | нет | — |
| `PasswordResetRequired` | `boolean` | нет | — |
| `Phone` | `text` | да | — |
| `RoleId` | `bytea` | нет | — |
| `Telegram` | `text` | да | — |
| `TotpSecret` | `text` | да | — |
| `Vk` | `text` | да | — |

Индексы:

- UNIQUE (`ClientId`); WHERE `"ClientId" IS NOT NULL`.
- UNIQUE (`EmailBlindIndex`).
- (`RoleId`).

Внешние ключи:

- `ClientId` → `Clients.Id`; ON DELETE SetNull.
- `RoleId` → `Roles.Id`; ON DELETE Cascade.

### VacationRequests

Сущность `VacationRequest`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `DecisionMessage` | `character varying(500)` | да | — |
| `ProcessedAtUtc` | `timestamp with time zone` | да | — |
| `ProcessedBySuperuserId` | `bytea` | да | — |
| `RequestMessage` | `character varying(500)` | да | — |
| `RequestedEnd` | `timestamp with time zone` | нет | — |
| `RequestedStart` | `timestamp with time zone` | нет | — |
| `RequesterId` | `bytea` | нет | — |
| `RequesterPrincipalType` | `text` | нет | — |
| `ResultingVacationId` | `bytea` | да | — |
| `Status` | `text` | нет | — |
| `SubjectId` | `bytea` | нет | — |
| `SubjectType` | `text` | нет | — |
| `Version` | `integer` | нет | — |

Индексы:

- (`RequesterPrincipalType`, `RequesterId`, `CreatedAtUtc`).
- (`SubjectType`, `SubjectId`, `Status`).

Ограничения:

- `CK_VacationRequests_Range`: `"RequestedStart" < "RequestedEnd"`.
- `CK_VacationRequests_Version`: `"Version" > 0`.

### WorkingHoursRequestDays

Сущность `WorkingHoursRequestDay`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `DayOfWeek` | `integer` | нет | — |
| `EndMinuteOfDay` | `integer` | нет | — |
| `IsWorkingDay` | `boolean` | нет | — |
| `StartMinuteOfDay` | `integer` | нет | — |
| `WorkingHoursRequestId` | `bytea` | нет | — |

Индексы:

- UNIQUE (`WorkingHoursRequestId`, `DayOfWeek`).

Внешние ключи:

- `WorkingHoursRequestId` → `WorkingHoursRequests.Id`; ON DELETE Cascade.

Ограничения:

- `CK_WorkingHoursRequestDays_Minutes`: `"StartMinuteOfDay" >= 0 AND "StartMinuteOfDay" < 1440 AND "EndMinuteOfDay" > 0 AND "EndMinuteOfDay" <= 1440 AND "StartMinuteOfDay" < "EndMinuteOfDay"`.

### WorkingHoursRequests

Сущность `WorkingHoursRequest`. PK: `Id`.

| Поле | Тип PostgreSQL | NULL | Дополнительно |
| --- | --- | --- | --- |
| `Id` | `bytea` | нет | — |
| `CreatedAtUtc` | `timestamp with time zone` | нет | — |
| `DecisionMessage` | `character varying(500)` | да | — |
| `ProcessedAtUtc` | `timestamp with time zone` | да | — |
| `ProcessedBySuperuserId` | `bytea` | да | — |
| `RequestMessage` | `character varying(500)` | да | — |
| `RequesterUserId` | `bytea` | нет | — |
| `Status` | `text` | нет | — |
| `SubjectUserId` | `bytea` | нет | — |
| `Version` | `integer` | нет | — |

Индексы:

- (`RequesterUserId`, `CreatedAtUtc`).
- (`SubjectUserId`, `Status`).

Ограничения:

- `CK_WorkingHoursRequests_Version`: `"Version" > 0`.

## DEFAULT из истории миграций

`Дополнительно` в каталоге выше отражает настройки модели snapshot. Для исходной схемы также учитываются явные `defaultValue` операций `AddColumn` в `Up`: часть из них не отражена как `HasDefaultValue` в snapshot. Они не должны теряться при воспроизведении старой схемы по исходникам. В `Down` встречаются другие defaults, они в эту таблицу не включены.

| Таблица.поле | defaultValue в применённом Up | Миграция |
| --- | --- | --- |
| `PasswordRestorationRequests.WasUsed` | false | `AddUsedFlagForPasswordRestorationRequests` |
| `Appointments.IsDeleted` | false | `AddIsDeletedToAppointments` |
| `Appointments.Status` | 0 | `MigrateToProperStatusesInAppointments` |
| `Clients.CreatedAtUtc` | `new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)` | `AddClientCreationDate` |
| `Clients.IsLeadClosed` | false | `AddLeadLifecycle` |
| `Services.IsConsultation` | false | `AddLeadLifecycle` |
| `UserOnboardingStates.DefinitionVersion` | 1 | `VersionOnboardingDefinition` |
| `ClientPortalLoginLinks.FailedPinAttempts` | 0 | `HardenClientPortalCredentials` |
| `RequestReplays.CallerId` | `new byte[0]` | `ScopeRequestReplaysToCallerAndPayload` |
| `RequestReplays.RequestFingerprint` | пустая строка | `ScopeRequestReplaysToCallerAndPayload` |
| `Users.PasswordResetRequired` | false | `Stage11GodModeAndSystemNotices` |

Минимальная дата клиента — техническое значение при добавлении поля, а не доказанная дата регистрации. Фактическое SQL-представление предельных дат и infinity нужно учитывать в чтении Npgsql; оно не выводится здесь из одного C# literal. Пустой `bytea` default у CallerId также не является валидным 16-байтовым ULID. Само наличие default не доказывает наличие таких значений в production.

## Ссылки, не защищённые FK

Наличие поля с ID не гарантирует ссылочную целостность. При загрузке нужны проверки следующих ссылок и их перепривязка через типизированную карту ID:

- `VacationRequests.RequesterId`, `SubjectId`, `ProcessedBySuperuserId`, `ResultingVacationId`: тип владельца/отпуска определяется строковыми discriminator, отдельного FK нет.
- `WorkingHoursRequests.RequesterUserId`, `SubjectUserId`, `ProcessedBySuperuserId`: FK нет; FK есть только от дочерних дней к заявке.
- `AuditLogs.ActorUserId`, `EntityType`/`EntityId` и потенциальные ссылки в `Details`: FK нет, автор может отсутствовать, ID строковый и не обязательно ULID.
- `Notifications.ReferenceType`/`ReferenceId`, `DeepLink`: FK нет. `ReferenceId` — nullable ULID в `bytea`, тип объекта задаёт строковый ReferenceType; DeepLink может содержать старые ID.
- `RequestReplays.CallerId`, `ResponseEntityId`: ULID в `bytea`, без FK. Тип ответной сущности определяется старым endpoint; ключи обработанных запросов относятся к старому формату payload.
- `PasswordRestorationRequests.Email`: привязка по email, без FK пользователя.
- `RecurringTaskExecutions.DeduplicationKey`: строка с типом задачи, ID и временными компонентами. FK отдельных полей не переписывает этот ключ автоматически.
- `CalendarSubscriptions`: оба FK nullable, CHECK ровно одного владельца нет. `ClientContacts` может быть общей строкой для нескольких клиентов: уникальности `Clients.ContactsId` нет.

## Таблицы вне EF

### История EF

`__EFMigrationsHistory` хранит `MigrationId` и `ProductVersion`. Полный ожидаемый набор идентификаторов — в каталоге миграций ниже. Таблица является свидетельством версии источника, а не историей применения нового runner. В целевой БД используется собственный `schema_migrations`; старый журнал сохраняется в материалах переноса, но не выдаётся за применённые новые шаги.

### Quartz

Источник — `MelodyTrack.Init/quartz.sql`; таблицы создаются старым Init после `MigrateAsync` и проверки/перешифрования персональных данных.

| Таблица | Содержание / ключевые связи |
| --- | --- |
| `qrtz_job_details` | Описания заданий; PK `(sched_name, job_name, job_group)`; тип задания и job_data |
| `qrtz_triggers` | Следующие/предыдущие запуски, состояние и FK на job_details |
| `qrtz_simple_triggers` | Повторы, интервал, счётчик; FK на triggers |
| `qrtz_simprop_triggers` | Типизированные свойства триггера; FK на triggers |
| `qrtz_cron_triggers` | Cron и timezone; FK на triggers |
| `qrtz_blob_triggers` | Сериализованный payload; FK на triggers |
| `qrtz_calendars` | Сериализованные календари, PK `(sched_name, calendar_name)` |
| `qrtz_paused_trigger_grps` | Приостановленные группы триггеров |
| `qrtz_fired_triggers` | Активные/начавшиеся срабатывания и экземпляры исполнителя |
| `qrtz_scheduler_state` | Check-in экземпляров планировщика |
| `qrtz_locks` | Служебные блокировки планировщика |

Старый `Program.cs` регистрирует `CreateRecurringAppointments` и его cron-триггер. Предложение для новой БД — создавать Quartz заново из конфигурации новой версии, без копирования выполняющихся триггеров/блокировок и старых имён CLR. Окончательная политика служебных данных и возобновления фоновой работы описывается в карте переноса; отсутствие импорта этих строк не разрешает потерять предметные серии и обработанные задачи.

## Каталог 49 миграций

Порядок по идентификатору. Версия ниже взята из designer модели, где он есть; это не полный `ProductVersion` production-журнала. У `AddSavedClientPortalIdentities` в сообщении пользователя указана `10.0.11`, хотя designer содержит `10.0.8`. Три миграции без designer проверяются по атрибутам в самих файлах.

| MigrationId | Версия модели designer |
| --- | --- |
| `20251016195209_Initial` | 9.0.10 |
| `20251017094429_UpdateValuesOfRolesEnum` | 9.0.10 |
| `20251017112718_AddTableForSessions` | 9.0.10 |
| `20251017192336_AddTableForPasswordRestorationRequests` | 9.0.10 |
| `20251017192507_AddUsedFlagForPasswordRestorationRequests` | 9.0.10 |
| `20251024185123_CreateClientsTable` | 9.0.10 |
| `20251106073014_AddSomeImportantTables` | 9.0.10 |
| `20251107132104_AddPatronymicToClient` | 9.0.10 |
| `20251107141144_AddFuzzySearchExtension` | 9.0.10 |
| `20251107235804_RemoveProviderColumnFromService` | 9.0.10 |
| `20251108000022_AddExplicitTableForRecurrenceTypes` | 9.0.10 |
| `20251109210734_AddExpensesTable` | 9.0.10 |
| `20251110223354_AddOptionalProviderToAppointment` | 9.0.10 |
| `20251114191522_AddOptionalProviderToAppointmentRecurrenceRule` | 9.0.10 |
| `20260510150723_AddIsDeletedToAppointments` | 10.0.7 |
| `20260512215500_AddAuditLogsTable` | нет designer |
| `20260513120000_AddAuditLogSourceIpAddress` | нет designer |
| `20260513123000_AddRequestReplays` | нет designer |
| `20260519172815_AddExpensesCategory` | 10.0.7 |
| `20260521153324_AddClientSourcesMarkExpensesAsSetNullOnDelete` | 10.0.7 |
| `20260521162742_MigrateToProperStatusesInAppointments` | 10.0.7 |
| `20260521171001_AddUserAvailability` | 10.0.7 |
| `20260522161021_AddClientCreationDate` | 10.0.7 |
| `20260522180320_AddUserOnboardingState` | 10.0.7 |
| `20260531193355_AddBirthDateToClientAndContactsToUser` | 10.0.7 |
| `20260604192227_AddRecurringTasks` | 10.0.7 |
| `20260604212129_AddSupportOfTeachersScheduleToRecurringTasks` | 10.0.7 |
| `20260605144526_AddRecurringTaskDelayAndCancellation` | 10.0.7 |
| `20260606095140_AddDebtorRecurringTaskRules` | 10.0.7 |
| `20260606103508_AddCustomTasks` | 10.0.7 |
| `20260606172557_RevokeExistingSessionsForRefreshTokenHardening` | 10.0.7 |
| `20260606174424_InvalidateExistingPasswordResetRequestsForTokenHardening` | 10.0.7 |
| `20260607120615_WidenClientContactColumnsForEncryptedStorage` | 10.0.7 |
| `20260607123407_AddEncryptedUserEmailBlindIndex` | 10.0.7 |
| `20260720193842_AddClientVacations` | 10.0.8 |
| `20260721035822_AddLeadLifecycle` | 10.0.8 |
| `20260721162449_AddCalendarSubscriptions` | 10.0.8 |
| `20260721174045_AddServicePublicName` | 10.0.8 |
| `20260722222015_AddCoursesAndClientPortal` | 10.0.8 |
| `20260724052729_ScopeRequestReplaysToCallerAndPayload` | 10.0.8 |
| `20260726194402_VersionOnboardingDefinition` | 10.0.8 |
| `20260727184520_HardenClientPortalCredentials` | 10.0.8 |
| `20260731181524_AddSavedClientPortalIdentities` | 10.0.8 |
| `20260823103001_Stage8AuthenticationCutover` | 10.0.11 |
| `20260829135816_Stage11GodModeAndSystemNotices` | 10.0.11 |
| `20260830111037_Stage12NotificationsAndWebPush` | 10.0.11 |
| `20260830173742_Stage13VacationRequests` | 10.0.11 |
| `20260901191134_Stage13WorkingHoursRequests` | 10.0.11 |
| `20260904141137_Stage14TimedVacations` | 10.0.11 |

## Существенные преобразования уже применённого источника

| Миграция / механизм | Значение для импорта |
| --- | --- |
| `MigrateToProperStatusesInAppointments` | Удалила IsCanceled/IsCompleted и добавила Status с default 0, без переноса прежних флагов в Up. Импорт читает сохранённый integer текущего источника; утраченные до этой миграции флаги из него не восстанавливаются |
| `WidenClientContactColumnsForEncryptedStorage`, `AddEncryptedUserEmailBlindIndex`, personal-data backfill Init | Контакты могут быть зашифрованы; blind index не является email и не переносится как контакт |
| `RevokeExistingSessionsForRefreshTokenHardening`, `InvalidateExistingPasswordResetRequestsForTokenHardening` | Отзыв/использование сохраняются; обработчики `Up` не запускаются повторно при импорте |
| `ScopeRequestReplaysToCallerAndPayload` | Служебная история replay была очищена, затем ограничена вызывающим и fingerprint |
| `Stage8AuthenticationCutover` | Отозвала сессии, закрыла reset-запросы, пометила пароли сотрудников для сброса, очистила saved identity references и токены/PIN клиентских ссылок; последующие действующие данные могли появиться заново |
| `Stage13VacationRequests`, `Stage13WorkingHoursRequests` | Добавили заявки с полиморфными/логическими ссылками и Version; версия не заменяет предметную историю решений |
| `Stage14TimedVacations` | Границы уже `timestamptz`, конец уже исключительный: старую включительную дату сдвинули на сутки при применении миграции. Повторять сдвиг нельзя |

## Основные различия с текущими моделями rewrite

- Старый источник не содержит `Rooms`, единой `Vacations`, `SchedulingInterruptions`, `Appointments.RecurrenceOrdinal`, длительности/часового пояса правила. Описания этих объектов из более поздней ветки не являются production-схемой.
- В источнике нет таблиц покупок абонементов, явных списаний за занятия, возвратов, маркетинговых кампаний, попыток практики и публичных профилей преподавателей. Их нельзя заполнять вымышленной историей.
- Старая услуга не содержит длительность, а старое занятие хранит отдельное окончание. Новая услуга/серия требует длительность; новая встреча требует сотрудника, хотя старый ProviderId nullable.
- Курс хранит блоки/ветки/порядок и BBCode; новое дерево тем плоское, документ типизированный. Текущие модели не покрывают все поля источника.
- Старый баланс вычислялся по цене на начало встречи, новый факт списания и новые правила используют окончание. Это различие нужно разрешить до создания исторических списаний.
- Полный список преобразований, пробелов и проверяемых соответствий — в [карте переноса](migration-map.md).
