# Текущая задача

## Цель и статус

Завершено: расширение `rerun-failed` на frontend/browser. Выборка использует файл/путь имени Vitest или проект/файл/путь названий Playwright, сохраняет исходный browser-порт и сверяет выполненные тесты. Неоднозначные идентичности отклоняются; повтор отмечается `subset_passed` и не заменяет исходный отчёт. Ветка `design/melodytrack-v2`. По поручению пользователя результаты оформлены отдельными коммитами SSE и инструментов проверок; push не выполнялся.

Предыдущая порция SSE по D-478 и исправление фонового watchdog завершены и зафиксированы коммитом `4ca20f7`; Kiota сохранён.

## Результат runner и проверки

- Проверки расширения rerun: Python unittest 55/55 passed (`.artifacts/rerun-failed-unit.log`), структурная проверка 5 skills / 8 agents / 0 ошибок, `git diff --check` прошёл. Полный frontend pipeline: success 173/173, `.artifacts/agent-checks/20261115T171553-407818ef/report.json`; мутаций форматтера не было, существующий Vite chunk warning остался. C#/MSBuild не менялись.
- Настоящий Vitest: синтетическое падение `20261115T171518-28c4268e` → CLI rerun `20261115T171519-1776e6d2`: subset_passed, 1 passed / 1 skipped, похожий соседний тест не выполнялся. Настоящий Playwright (Chromium, порт5189, elevated): падение `20261115T171535-c31c0772` → CLI rerun `20261115T171536-1e915302`: subset_passed, только 1/1 выбранный тест; другие проекты не проверялись. Временные синтетические файлы удалены. Отчёты — `.artifacts/agent-checks/<run-id>/report.json`.
- Сводка ограничена статусом, счётчиками, ссылкой на отчёт и индексами ошибок; `details` раскрывает сообщения/stack по запросу. Полные логи, нативный JSON и browser traces сохраняются в `.artifacts/agent-checks/<run-id>/`. Нулевые, несогласованные и незавершённые результаты не дают success; ненулевой код pipeline сохраняется даже при успешных тестах. Flaky Playwright учитывается как ошибка. `all` остался backend-only. Документация: `docs/agent-workflow.md`, `web/README.md`, skill rewrite-verification.
- `python3 -m unittest discover -s scripts/tests -v`: 44/44 passed, лог `.artifacts/frontend-runner-tests.log`. `check_agent_infrastructure.py`: 5 skills / 8 agents, 0 ошибок. `plan` обоих scopes и `summary --run` проверены.
- Итоговый `run --scope frontend`: success, 173/173 tests, Biome/typecheck/Vite build; `.artifacts/agent-checks/20261115T165804-1349953b/report.json`. Мутация Biome в `vite.config.ts` просмотрена; остался существующий Vite chunk warning.
- Итоговый elevated `run --scope browser --browser-project chromium --browser-port 5189`: success, 11/11 passed; `.artifacts/agent-checks/20261115T165831-dec0c6c2/report.json`. Остальные browser-проекты этим запуском не проверены. Новых C#/MSBuild-правок в порции runner нет, backend не перезапускался. `git diff --check` прошёл.

## Контекст завершённой порции SSE

- Требования: D-478 в `../../../specification/decisions.md`, раздел автоматического получения изменений в `../../../specification/architecture.md`, текущий запрос пользователя о SSE и сохранении состояния UI.
- Технический контракт: `/api/changes/events`, авторизованный SSE с обезличенным `invalidate`, в том числе сразу после каждого подключения. После события клиент обновляет идентичность/права и перечитывает данные через Kiota; reconnect покрывает пропущенные изменения без журнала событий.
- Kiota регенерирован; transport, session/app и потребители реализованы. Экспоненциальный reconnect, heartbeat watchdog, online/visibility recovery и повторная синхронизация не используют polling API. Клиенты и открытые release notes удерживают видимое состояние при фоновой загрузке, включая смену версии; отозванный доступ убирает недоступные данные.
- Независимое ревью исправлений backoff и живого браузерного потока не выявило новых дефектов. Технические границы — [контракт](../live-data.md).
- Исправлен watchdog SSE после фонового режима: отсутствие прочитанных чанков в скрытой вкладке/неактивном окне больше не закрывает соединение; при возвращении начинается новый полный срок ожидания heartbeat. Добавлены регрессии длительного фона, возврата фокуса и восстановления действительно молчащего потока. Обычные `restore`/`me` при фокусе не пересоздают SSE. Истечение исходного bearer через пять минут по D-089 остаётся отдельной ожидаемой причиной reconnect.

## Проверки SSE до изменения runner

- `agent_checks.py plan/run --scope build`: итоговая сборка всей solution success, без предупреждений/ошибок; `.artifacts/agent-checks/20261115T161556-25358396/report.json`.
- `agent_checks.py plan/run --scope api`: 166/166 passed, без пропусков; `.artifacts/agent-checks/20261115T161538-4e118c25/report.json`. Обе dotnet-проверки выполнены в elevated shell.
- Frontend после исправления watchdog: `npm run verify:fix` через fnm Node26 — Biome/typecheck, 26 наборов / 173 теста, Vite build success. Мутации форматтера просмотрены; финальный повтор без мутаций. Лог `web/.artifacts/sse-focus-verify.log`. Осталось предупреждение Vite о чанке >500 kB.
- Browser: полный запуск — Chromium 11 passed, WebKit desktop/mobile 22 не запустились из-за отсутствия системных libicu74/libxml2. Отчёт `web/.artifacts/browser/report.json`, лог `run.log` рядом. Повтор всех Chromium-сценариев после финальных правок — 11/11 passed, `web/.artifacts/browser/chromium-report.json`, `chromium-run.log` рядом.
- Регрессия watchdog/focus после исправления: Chromium и Firefox — 2/2 passed. Сценарий с Playwright clock моделирует две минуты неактивности окна, возврат фокуса, `restore`/`me` без нового SSE и последующее обновление через прежний поток с сохранением ввода, фокуса и прокрутки. Отчёт `web/.artifacts/browser/sse-focus-report.json`, лог `sse-focus-run.log`. Совместимый Firefox установлен через Playwright; старый локальный browser build был несовместим с протоколом runner. Это целевой сценарий, не полный повтор browser suite. В логе есть предупреждение Ant Design Wave о setState при render; сценарий проходит.
- Порт5174 занят; browser использовал временную конфигурацию с теми же проектами и порт5189. Временный конфиг удалён, продуктовая конфигурация не менялась. `git diff --check` прошёл.

## Следующее действие

Реализация и создание коммитов завершены; дальнейшая задача не задана. `python3 scripts/agent_checks.py rerun-failed --run <run-id>` теперь поддерживает frontend/browser; старые отчёты без идентичностей требуют нового scope-запуска. Ограничения предыдущего SSE: WebKit требует системные библиотеки; публикация уведомлений ограничена одним процессом API.
