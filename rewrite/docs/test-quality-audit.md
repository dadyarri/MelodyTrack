# Аудит всех автотестов rewrite

## Область и способ оценки

Область — весь существующий набор rewrite: 97 C# test-классов в трёх проектах (60 Domain/Application, 13 Api, 24 Init/Persistence), 25 Vitest-файлов, браузерные сценарии и два Python test-модуля. Старый монорепозиторий в этот аудит не входит.

Проверены inventory сценариев, наблюдаемые assertions, границы подмен, изоляция, ошибки, права, время, отмена, гонки и откат. Детальное чтение и статический разбор возможных мутаций сосредоточены на рискованных границах; это аудит всех тестовых областей, а не построчная сертификация каждого assertion. Actual mutation testing и line/branch coverage не выполнялись; процент покрытия и mutation score не заявляются.

Источники ожиданий: [спецификация](../../specification/README.md), [решения](../../specification/decisions.md), [каталог клиентов](client-directory.md), [авторизация](authentication-api.md), [обучение](../../specification/employee-training.md), [контракт даты/времени](../web/src/shared/ui/date-time/README.md). Код и тесты показывают фактическое поведение и не утверждают новые бизнес-правила.

## Полная матрица

| Часть | Разбор |
| --- | --- |
| Domain/Application, все API и SQL/Init test-классы | [Backend: предметные области, транзакции, авторизация, импорт, криптография](test-audit-backend.md) |
| Session provider, регистрация/вход, HTTP/contract, passkeys, invitation, releases | [Session: сценарии, гонки и границы подмен](test-audit-session.md) |
| Каталог и его mapper, обучение и progress/demo, DataView, calendar/DateTime, scroll boundary | [UI: сценарии и assertions](test-audit-ui.md) |
| `appearance.test.ts` | Сохранённая light/dark тема выше OS preference; повреждённое значение и SecurityError чтения/записи дают безопасный fallback. Globals/storage очищаются. |
| `AppErrorBoundary.test.tsx` | Рабочий экран; исключение заменяется безопасным alert и действием восстановления, исходный текст ошибки не виден. Сам reload не исполняется в jsdom. |
| `normalize-kiota-optionals.test.ts` | Синтаксические преобразования optional/null, guard, idempotence и отказ при неожиданном шаблоне. |
| `generate-api-client.test.ts` | Исполняется настоящий orchestration script с fake dotnet в отдельном temp-каталоге: порядок tool restore/build/generate/normalize, include-path всех поддержанных маршрутов и остановка на каждой ошибке. Не проверяет сам Kiota бинарник. |
| `test_agent_checks.py` | Native XML, суммы нескольких assemblies, редкие ошибки больших отчётов, повреждённый/пустой/отсутствующий отчёт, несовпадающие счётчики; CLI plan/run/details/rerun, safe paths, build failure и продолжение независимых test lanes. Subprocess подменяется, настоящий backend run подтверждён отдельно. |
| `test_agent_infrastructure.py` | Валидный inventory; отсутствие skill/agent, неправильный frontmatter/identity, неизвестный skill, broken link, лишний override и невалидный TOML; каждый сценарий изолирован temp-каталогом. |
| `e2e/browser.spec.ts`, `cookie-server.ts` | Реальные браузеры: cookies/reload/logout через настоящий локальный HTTP-сервер (включая получение Cookie сервером), две вкладки/Web Locks/BroadcastChannel, настоящие IndexedDB transactions/merge/owner isolation/abort/quota, клавиатурный фокус и узкий viewport, chooser user gesture/cancel, native Chromium WebAuthn creation/assertion/abort, App composition и Vite redirect, native popup wheel. |

## Пробелы прежнего аудита G1–G12

| ID | Добавленное доказательство |
| --- | --- |
| G1 | ClientsPage использует настоящий DataView, mapper и generated Kiota с синтетическим HTTP: query, archive/source, multi-sort, page/page size, reset и детали контактов/московская дата. |
| G2 | Старый ответ приходит при незавершённом новом запросе: исходный signal отменён, старые строки не появляются, loading сохраняется; отдельные retry list/options. |
| G3 | Входящий changed BroadcastChannel отменяет pending GET и отвергает поздний результат; неизвестное сообщение игнорируется. Настоящая доставка и межвкладочный lock проверяются браузером. |
| G4 | Учебное упражнение проходит пользовательскими действиями: правильная сортировка/результат, завершение/save/reload/repeat, defer, storage failure и смена аккаунта. |
| G5 | SQL email/link принадлежат разным клиентам; несколько контактов проверяются по owner/ID/kind/name/format/value/comment/primary; HTTP DateOnly/UTC и no-store options. |
| G6 | q 200/201 на Application/HTTP границе, 2/3 цифры телефона, пустые и непересекающиеся стабильные страницы; отмена во время store I/O. |
| G7 | Общий auth POST helper проверяет refresh cookie/token и атрибуты на выдаче, отсутствие refresh-cookie на challenge-only (отдельная browser cookie допустима); restore rotation/отказ/clear, logout и IPv6 loopback. |
| G8 | Restart во время authenticator setup, passkey chooser и final complete: abort и игнорирование поздних результатов, без восстановления старого шага. |
| G9 | Настоящий IndexedDB: stale-tab merge, разные владельцы/reload, pre-abort/in-flight abort, quota и transaction abort без ложного сохранения. |
| G10 | Home/End/PageUp/PageDown/Shift+год, min/max selectable, DateTime bounds и clear; браузерное повторное открытие и возврат фокуса. Найден и исправлен дефект быстрого повторного открытия календаря. |
| G11 | Добавлен отдельный browser pipeline Chromium, desktop WebKit и mobile WebKit. Браузерные механизмы проверяются реально; HTTP business outcomes синтетические. Полный браузер→реальный API→PostgreSQL acceptance и аппаратная Safari/WebAuthn-приёмка остаются за пределами этого набора. |
| G12 | Release notes при отказе storage/смене аккаунта; orchestration генератора и реальный Vite loopback redirect с сохранением пути/query и no-store. |

## Итоговые проверки

| Полный набор / команда | Фактический результат |
| --- | --- |
| `python3 scripts/agent_checks.py plan --scope all`, затем elevated `run --scope all` | Exit 0; вся solution собрана, 0 warnings/errors. Domain 512, Api 162, Init 340: **1014 passed**, 0 failed/skipped/not_run/errors. [Отчёт](../.artifacts/agent-checks/20261115T151358-5188e38a/report.json). |
| `fnm exec --using 26 npm run verify:fix` из `web/` | Exit 0; Biome, TypeScript, **155 passed в 25 файлах**, production build. Мутации форматтера просмотрены. Сохранённое предупреждение Vite: общий chunk 657 КБ до gzip. Локальный лог `/tmp/melodytrack-web-audit-verify.log`. |
| `npm run test:browser`, Node 26.7 / Playwright 1.65.1 | Exit 0; Chromium, desktop WebKit, mobile WebKit: **30 passed / 3 skipped / 0 failed** из 33. [JSON](../web/.artifacts/browser/report.json); лог `/tmp/melodytrack-browser-docker.log`. |
| `python3 -m unittest discover -s scripts/tests -v` | Exit 0; **31 passed**. Лог `/tmp/melodytrack-python-audit.log`. |
| `python3 scripts/check_agent_infrastructure.py`, `git diff --check` | Exit 0; 5 skills, 8 agents, 0 structural errors; diff без whitespace ошибок. |

Backend и браузерные проверки выполнены после исправлений полными наборами, без подмены успешным subset. Native xUnit XML и build/test logs находятся рядом с backend JSON. Игнорируемые локальные артефакты не входят в коммиты.

Браузеры запущены в контейнере `medtracker-playwright:1.64.1` с Ubuntu-библиотеками и подключёнными **текущими** Node 26.7, node_modules и браузерами Playwright 1.65.1: версия в имени образа не является версией runner. На хосте CachyOS отсутствуют совместимые WebKit ABI. Три явных пропуска: virtual authenticator через Chromium CDP недоступен в двух WebKit-проектах; mouse wheel недоступен в mobile WebKit и проверяется desktop WebKit/Chromium. Синтетический chooser на всех трёх проектах проверяет user activation, но не аппаратный passkey.

## Вывод о качестве и границах

Набор проверяет результаты и отказы, а не только успешный HTTP status: SQL filtering/count/page, financial invariants, permissions, transactional rollback, concurrent replay, сессионную ротацию, stale responses и browser persistence. Убраны подмены DataView/API на странице каталога; новые межслойные assertions отличают перепутанные контакты и неправильную сериализацию от корректного результата.

Расширение охватывает все обнаруженные конкретные G1–G12 на указанных уровнях. Это не доказательство отсутствия любых будущих пробелов. В подробных матрицах отдельно перечислены ограничения: комбинаторная полнота прав/переходов, property-based арифметика, полный cancellation call graph, нагрузка, эксплуатационный scheduler и внешние transport/device сценарии. Нельзя придумывать их бизнес-ожидания или выдавать отсутствие ещё не реализованного слоя за тест успешно работающего слоя.

Production-данные, импорт реальной базы и внешние провайдеры не использовались. Синтетический browser HTTP не заменяет настоящие PostgreSQL API/Init tests; в совокупности эти уровни сильнее каждого отдельно, но не составляют полного deployment acceptance.
