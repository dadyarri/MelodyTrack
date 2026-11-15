# Аудит автотестов каталога клиентов и браузерных сессий

## Область и метод

Проверена вся прикладная порция коммитов `f6fdba7` и `2a4d19a`: каталог клиентов (Application/SQL/API/React), cookie restore, изменения регистрации/входа, обучение, история релизов, общие таблица, дата/время и ограничение прокрутки, генерация клиента и конфигурация Vite. Рассмотрены соответствующие backend-тесты и все 22 frontend test-файла, включая `web/scripts/normalize-kiota-optionals.test.ts`. Старые предметные области вне этой порции не являются объектом полного аудита.

Метод: независимый разбор ожидаемых исходов, сопоставление с assertions и статический анализ гипотетических мутаций. Серверный каталог, сессии и остальные frontend-компоненты проверены раздельно; итог откалиброван основным агентом. Исходники и тесты при аудите не изменялись. Настоящие мутации, измерение line/branch coverage и браузерные прогоны не выполнялись. Ни процента покрытия, ни фактического mutation score этот отчёт не устанавливает.

Нормативные основания: CLI-011–CLI-021/D-418, D-519, D-537, D-513/D-538 и ссылки из [контракта каталога](client-directory.md); [контракт авторизации](authentication-api.md); [требования обучения](../../specification/employee-training.md); [контракт компонентов даты/времени](../web/src/shared/ui/date-time/README.md). Тесты и код не заменяют эти источники.

## Заключение

Тесты содержательные: проверяют настоящие PostgreSQL-запросы, реальные границы поиска, отказ в доступе, ротацию сессий и несколько управляемых гонок. Это существенно сильнее простых проверок успешного статуса. Однако полнота недостаточна для утверждения, что вся порция защищена от регрессий: самые заметные пробелы — связь страницы клиентов с реальными компонентами/API, входящие межвкладочные события, завершение учебного упражнения и физическое браузерное поведение.

Подтверждённых дефектов приложения в рамках статического аудита не установлено. Перечисленные ниже сценарии — пробелы доказательства, а не утверждение, что реализация уже работает неверно. P1 означает высокий приоритет усиления тестов, а не установленную уязвимость.

## Фактические запуски

- Сохранённый запуск `20261115T142820-e05b501c`: вся C# solution собрана без предупреждений/ошибок; Domain 509/509, API 157/157, Init 90 успешны и 245 ошибок общей PostgreSQL-фикстуры. Отчёт `.artifacts/agent-checks/20261115T142820-e05b501c/report.json`.
- Причина ошибок — невозможность создать Docker veth после обновления модуля. Пользователь перезагрузил систему, самостоятельно повторил тесты и сообщил об успехе. Команда, число тестов и артефакты этого повтора агентом не подтверждены.
- Последний frontend `verify:fix`: Biome, TypeScript, 126/126 тестов в 22 файлах, production build успешны. Лог предыдущей сессии: `/tmp/melodytrack-web-verification.log`.
- Во время этого статического аудита продуктовые проверки повторно не запускались: код не менялся. Успех прежних запусков не используется как доказательство отсутствия пробелов.

## Матрица требований и доказательств

| Граница | Существующее доказательство | Что остаётся непроверенным |
| --- | --- | --- |
| Серверный доступ к каталогу, D-519 | `ClientDirectoryServiceTests.Read_UnsupportedGrant_DeniesBeforeReadingStore`, API 401/403, запрет Own/Today | UI отказа и отзыв права при открытой странице |
| Валидация параметров | Domain/API отрицательные сортировки, page/pageSize, конфликт источника, безопасная ошибка | Независимые граничные 200/201 символ поиска, 0/1/100 pageSize и HTTP source/archive матрица |
| Полное ФИО и Levenshtein | PostgreSQL `Read_UnifiedName_UsesDatabaseLevenshteinWithThreeEditBoundary`, 3/4 правки, регистр/пробелы, отсутствие отчества, длинное имя | Дополнительные языковые варианты — по требованиям; не объявлять их дефектом автоматически |
| Защищённые контакты | `Read_ProtectedPhoneSearch_FiltersBeforeTotalAndPagination` проверяет значение телефона и нужный ID | 2/3 цифры для нормализованного телефона, email/link, комментарии, несколько контактов |
| Архив, источники, мультисортировка | PostgreSQL проверяет ID результата, статус, приоритет, ID tie-breaker, null-last source asc/desc | Отдельные birthdate asc/desc/null, связка нескольких фильтров, обычная вторая полная страница |
| Подсчёт и страницы | Поиск до count/page, запрос страницы за концом; default 10 | Непересекающиеся полные страницы без поиска; пустой набор total/pages=0 |
| Справочники и JSON | Options store сохраняет исторический вид контакта; список API имеет no-store | Успешный HTTP options/no-store, все поля карточки/контакта, UTC/DateOnly wire format |
| Миграция 0015 | Store-тесты применяют SQL и реально вызывают функцию расширения | Проверка полного migration runner с новой миграцией отдельно от ручного набора SQL в fixture |
| Cookie и Origin | `StaffSessionCookiesTests`: HttpOnly, Strict, path, MaxAge, Secure; trusted/untrusted Origin; интеграционный отказ не расходует сессию | IPv6 loopback, endpoint-матрица token/challenge outcomes, ошибка logout/restore без удаления cookie |
| Restore/refresh | Реальный PostgreSQL/Kestrel: ротация, отзыв/истечение, неактивный контекст, обязательный фактор; UI temporary failures и смена identity | Malformed restore response как весь поток; настоящий cookie jar браузера |
| Гонки сессии | StrictMode singleflight, refresh+GET, отмена ожидающего GET, restore+sign-in, refresh+logout, ограничение retry | Входящий BroadcastChannel, две вкладки с настоящим Web Lock, все подписчики размонтированы до завершения restore |
| Страница клиентов | Два теста: search сбрасывает page без localStorage, новые loading/error очищают строки | Фильтры/sort/size/retry/options, stale responses, drawer/actions, реальные generated GET |
| Общая таблица | `DataView.test.tsx`: порядок сервера, вторичный sort, desc→off, независимые кнопки строки, loading/empty/error/retry, page/search callback | Удаление ключа из нескольких сортировок с сохранением оставшихся приоритетов; выделение текста/links; layout и touch |
| Модели ответа каталога | Отсутствующий источник/контакты, DateOnly/UTC mapping, плохая пагинация | Контакт с полями и ошибками, invalid booleans/numbers/dates, options parser, слишком много items |
| Регистрация и WebAuthn | Роли/обязательные средства, последовательные этапы, пропуски, отклонённый TOTP; helper начинает WebAuthn синхронно | Restart во время I/O, rejection/abort native chooser, неуспешный финальный complete в UI, настоящая активация пользователя |
| Обучение, D-513/D-538 | Модели упражнений/merge/migration, запрет реального fetch, статья не завершает упражнение, отзыв права закрывает UI | Успешное выполнение через UI, сохранение и повторное открытие; ошибка IndexedDB и смена аккаунта во время I/O |
| История релизов | Парсинг, сбор страниц, ранняя остановка, смена deployment, retry, account-owned acknowledgement | Смена аккаунта при незавершённом чтении/открытой модалке, отказ хранилища в связке UI |
| Дата и время | Gregorian/leap, Moscow midnight, wall clock, inclusive bounds в модели; typed input/keyboard в UI | Полный обещанный keyboard contract; составные min/max, очистка, реальные фокус/zoom/mobile |
| Ограничение прокрутки | `popupScrollBoundary.test.ts`: жесты, границы и cleanup | Настоящие wheel/touch/overscroll и вложенные порталы браузеров |
| Генерация и app shell | Normalizer проверяет optional syntax/idempotence; HTTP tests проверяют Kiota WebAuthn serialization и safe errors | Include-path нового API, generated query/date round-trip, App routing/recovery/sign-out composition, browser Vite redirect |

## Приоритетные пробелы

### G1 — P1: каталог проверяется через подменённую таблицу

Место: `web/src/pages/clients/ui/ClientsPage.test.tsx:31` и `ClientsPage.tsx:297`. Mock DataView показывает только поиск и переход страницы; фильтры, сортировки, retry, карточка и настоящие действия вообще не монтируются. Мутации `onSortChange` в no-op (`:317`), удаления `pageSize` из обновления (`:310`) или изменения source/archive callbacks (`:344`, `:359`) оставят оба теста зелёными. Unit-тесты DataView не проверяют привязку к странице.

Добавить интеграционный React-тест с настоящим DataView и generated HTTP через поддельный fetch: менять архив/источник, задать две сортировки, снять одну, поменять размер страницы и проверить параметры фактического GET и итоговые строки. Drawer должен показывать синтетические контакты и Moscow timestamp; phone/email должны оставаться самостоятельными действиями.

### G2 — P1: нет входящего межвкладочного события

Место: `StaffSessionProvider.tsx:589`. Оригинал обрабатывает `event.data === "changed"` через `clearLocal`. Удаление этого обработчика переживёт существующий `does not broadcast an account switch when the active account is only being restored`: он проверяет отсутствие отправки, не доставку сообщения.

Добавить тест с уже открытым аккаунтом и незавершённым GET: доставить `changed`, проверить anonymous/error, отмену запроса и отклонение позднего результата. Постороннее сообщение не должно сбрасывать аккаунт. Отдельно нужен браузерный сценарий двух вкладок с настоящим каналом.

### G3 — P1: не доказана защита страницы от перестановки ответов

Место: `ClientsPage.tsx:198`. Удаление проверки `signal.aborted` не обнаруживается двумя page-тестами: второй отвергает новый запрос, но не разрешает старый ответ после нового. Session tests подтверждают отмену при смене аккаунта, а не отмену при смене поисковых условий той же страницы.

Добавить два deferred ответа: новый поиск завершается первым, старый — последним; видим только новый результат, total и ошибку соответствующего запроса. Проверить abort полученного signal и что старое finally не выключает loading нового запроса.

### G4 — P1: успешное упражнение существует только как pure predicate

Место: `ClientDirectoryTraining.tsx`, `EmployeeTraining.test.tsx` / `client directory module`. UI-тест открывает список, нажимает неправильный результат и читает статью. Модель `clientDirectoryExerciseComplete` протестирована, но удаление вызова завершения из успешной UI-ветки останется незамеченным.

Пройти правильный поиск, источник, архив и порядок через настоящие контролы; нажать проверку и подтвердить сохранение completed с прежним access-progress и владельцем. Повторно загрузить прогресс. На всём пути fetch не вызывается. Проверить defer и неуспешное сохранение отдельно.

### G5 — P2: контакты, wire mapping и generated GET

Места: `ClientDirectoryStore.cs:97`, `clientDirectory.ts:114` и `clientDirectoryApi.ts:12`. Удаление расшифровки комментария/возврат null переживает store suite: комментарий записан в fixture, но не утверждается. Frontend model-тест использует только пустые contacts. Удаление `comment`, `isPrimary` или подмена format в mapper тоже не меняет эти ожидания. Page-тест подменяет getClientDirectory, поэтому не обнаруживает пропажу queryParameters в wrapper.

Нужны минимум два клиента с несколькими различимыми контактами: проверить owner/ID/kind/format/comment/primary/value, отсутствие межклиентной перестановки, email/link поиск. Через HTTP проверить новый generated маршрут и полный набор query params, включая явно пустой sort, DateOnly и UTC без локального смещения. Отрицательные payload должны давать safe invalid-response.

### G6 — P2: телефонная и пагинационная границы

Место: `ClientDirectoryStore.cs:108`: `digits.Length >= 3`. Мутация `>= 3` → `>= 2` переживает 11-значный запрос текущего теста. Выбрать 2/3 цифры, которые совпадают только после удаления форматирования: простое строковое Contains по контакту — отдельный допустимый путь, он не должен случайно сделать отрицательный тест положительным.

Обычная пагинация требует данных больше pageSize и проверки точных непересекающихся ID/total/pages для двух страниц; существующий beyond-end phone-test не заменяет этот сценарий. Отдельно проверить пустой результат и пределы query на Application/HTTP уровне.

### G7 — P2: частичное доказательство cookie-матрицы

Места: `StaffCredentialEndpoints.cs:80–89`, `StaffSessionEndpoints.cs` и cookie tests. Снятие всего общего cookie-filter уже обнаружится restore-тестом: для него нужна cookie первоначальной регистрации. Поэтому такую мутацию нельзя считать survivor. Но Set-Cookie каждого token-bearing flow (password/passkey/factor/refresh/resume) и отсутствие cookie в challenge-only ответах не имеют собственной матрицы assertions.

Добавить проверки значений/атрибутов cookie для представительных вариантов ответов и challenge-only отказа. Проверить, что неуспешный logout и временный restore не удаляют cookie. Cookie unit-тест дополнить IPv6 loopback. Это дополнительная защита разных ветвей, а не доказанный дефект общего фильтра.

### G8 — P2: restart регистрации во время I/O

Место: `StaffInvitationForm.tsx:222–232`. Удаление increment generation/abort из restart не задевает текущие UI-тесты. Helper-тест подтверждает передачу signal и синхронный вызов credentials.create, но не реакцию формы на его позднее завершение.

Запустить deferred setup/chooser/complete, нажать «начать заново», завершить старый запрос. Не должны восстанавливаться старые шаг, challenge, секрет или аккаунт. Проверить AbortError и отказ chooser без автоматического повторения одноразового complete.

### G9 — P2: IndexedDB подменена на границе UI

Место: `features/employee-training/model/storage.ts`. UI подменяет load/save; pure merge-тесты не исполняют IndexedDB transaction. Удаление `mergeTrainingProgress` из реального save с заменой на прямой put не будет замечено UI/pure tests, хотя устаревшая вкладка сможет затереть завершение.

Нужен браузерный сценарий двух владельцев и двух соединений: stale save не стирает completed; quota/blocked/abort не отображаются как успешная запись; account switch не применяет поздний progress. Не подменять это утверждением, что сервер сохранил данные.

### G10 — P2: клавиатурный контракт даты/времени шире UI-тестов

Место: `Calendar.tsx`, ветви PageUp/PageDown/Shift+год; контракт перечислен в README компонента. Удаление соответствующих ветвей переживает текущие UI-тесты со стрелками и Escape. Модель inclusive min/max уже проверена; это не пробел pure range-проверки.

Добавить UI-переходы Home/End/PageUp/PageDown/Shift+PageUp/PageDown/Alt+ArrowDown, точные selectable min/max дни, составные DateTime bounds и очистку. Проверять значение и фокус, а не внутреннее состояние компонента.

### G11 — P2: границы браузера и композиция приложения

Все 126 frontend-сценариев выполняются в jsdom. Они не доказывают реальную работу cookies, Web Locks, пользовательской активации WebAuthn, mobile layout/zoom, touch/scroll и Vite navigation. Подтверждение синхронного helper-вызова полезно, но не заменяет click→browser chooser в приложении.

Минимальный браузерный набор: вход→reload→restore→logout→reload; две вкладки/одновременный refresh; регистрация с доступным и отменённым passkey; каталог с настоящим API на синтетической БД и узким viewport; popup scroll/focus на поддерживаемом WebKit. API/Init fixtures должны оставаться изолированными от реальной базы.

### G12 — P3: история релизов и вспомогательные маршруты

У истории уже есть проверка страниц и смены deployment; отсутствие предела произвольной испорченной последовательности не объявляется дефектом без установленного контракта. Полезны component-сценарии смены аккаунта во время загрузки/модалки и отказа storage. Normalizer имеет собственные тесты idempotence/guard/syntax, но include-path в generate-api-client и Vite redirect отдельно не проверяются. Единственное изменение vite.config в порции — форматирование, не новая redirect-логика.

## Псевдомутации: сильные стороны

Ниже «обнаружится» означает прогноз по конкретным assertions, а не выполненную мутацию.

| Изменение | Тест, который обнаружит изменение |
| --- | --- |
| Levenshtein limit 3→2 или 4 | Store `Read_UnifiedName_UsesDatabaseLevenshteinWithThreeEditBoundary`: 3/4 суффиксных правки |
| Count/page до поиска контактов | Store `Read_ProtectedPhoneSearch_FiltersBeforeTotalAndPagination`: нужный клиент за первой исходной строкой |
| Null source вверх при desc | Store `Read_NullableSort_KeepsMissingValuesLast`: оба направления |
| Переставить приоритеты sort | Store `Read_MultipleSortColumns_UsesRequestedPriorityBeforeStableIdentifier`: конфликтующие порядки |
| Скрытый name sort вместо ID tie-breaker | Store `Read_EqualOrAbsentSortValues_UsesIdentifierWithoutHiddenExtraSort` |
| Разрешить Own или Today | Domain `Read_UnsupportedGrant_DeniesBeforeReadingStore`, API Own-scope cases |
| Отдавать исходный q в validation error | API `Read_InvalidQuery_ReturnsSafeValidationProblem`: нет private synthetic string |
| Cookie без HttpOnly/Strict/14 дней/path | Cookie `Write_EnvironmentAndTransport_UsesRestrictedPersistentCookie` |
| Принимать чужой Origin и расходовать сессию | `Restore_UntrustedOrigin_RejectsWithoutConsumingCookieSession` |
| Восстанавливать отозванную/неактивную сессию | `Restore_RevokedOrExpiredSession_ReturnsUnauthorized`, `Restore_InactiveBrowserSession_DoesNotReactivateRememberedAccount` |
| Терять restoring при 429/503 | Provider `keeps restoration recoverable after HTTP %i and retries explicitly` |
| Удваивать restore в StrictMode | Provider `shares one cookie restoration across StrictMode effect remounts without aborting rotation` |
| Бесконечно повторять GET после rotated-token 401 | Provider `does not refresh again when the resource rejects the rotated token`: call count и завершение сессии |
| Применять чужую refreshed identity | Provider `fails closed when refreshed identity belongs to another account` |
| Сортировать полученную страницу локально | DataView `requests sorting while preserving the order received from the server`: Beta перед Alpha |
| Перехватывать кнопку строки row handler | DataView `opens a clicked row but preserves independent actions inside its cells`: row вызван один раз |
| Засчитывать неправильный порядок учебных клиентов | Demo `requires demonstrated result…`: отдельный отрицательный порядок |
| Считать today по локальной дате вместо Москвы | Calendar `uses Moscow midnight for today regardless of the device timezone`: 20:59:59Z/21:00Z |
| Признать неполные токены рабочей сессией | Contract `rejects partial tokens instead of treating them as a session` |
| Показать произвольные Problem Details | HTTP `does not expose raw problem details or arbitrary trace text` |

Survivors и зоны отсутствующего покрытия перечислены в G1–G12 с исходной ветвью, изменением, причиной и конкретным дополнением. Это ограниченная осмысленная выборка, а не полный перечень всех синтаксических мутаций; делить количество строк таблицы на число пробелов и выдавать результат за mutation score некорректно.

## Изоляция и надёжность

- PostgreSQL fixture создаёт отдельную БД на сценарий, без обращения к реальной базе; идентификаторы для сортировки фиксированы. Случайный ключ шифрования не участвует в ожидаемом порядке.
- Cookie-сценарии используют настоящий HTTP/PostgreSQL; browser policy при этом не эмулируется автоматически серверным клиентом.
- Fake timers/deferred promises позволяют управлять временем/гонками; mocks, timers и globals в сессии сбрасываются. Исправленное ожидание начала fetch убирает зависимость от синхронности Kiota.
- Тест pre-cancelled Read в Application использует store, который сам проверяет отмену до счётчика. Удаление дублирующей проверки в service может остаться незаметным, но это не самостоятельный дефект: store всё равно обеспечивает тот же исход. Для доказательства propagation полезен spy полученного token и отмена уже ожидающей I/O, а не assertion на частную защитную строку.
- Нет проверки согласованного SQL read-only snapshot при конкурентном изменении источника/контакта и нет performance-границ. In-memory обработка всех кандидатов уже описана как ограничение реализации; не представлять suite как нагрузочную проверку.

## Порядок усиления

1. G1–G4: связка каталога, stale responses, входящее межвкладочное событие, правильное завершение упражнения.
2. G5–G9: контакты и HTTP-мэппинг, пограничные запросы, cookie outcomes, restart и настоящее persistence.
3. G10–G12: keyboard/browser composition и остальные отказы.

Отчёт завершает аудит; добавление тестов и изменение приложения не входили в поручение. После отдельной реализации усилений нужен полный соответствующий прогон, а не только запуск новых тестов.
