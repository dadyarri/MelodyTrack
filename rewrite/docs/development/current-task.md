# Текущие задачи

Ветка `design/melodytrack-v2`. PR #15/#16 объединены локально (`a12950e`,
`bed9290`); коллизия решения о Base UI разрешена с согласия пользователя:
его ID — D-565, прежние ID сохранены. Push не поручен.

| Задача | Статус | Следующее действие |
| --- | --- | --- |
| [Маскот-гитара](tasks/sign-in-mascot.md) | Завершён и сохранён отдельным коммитом: крупный ключ, кивок и пауза 650 мс; frontend 235/235, browser 51 passed / 3 skipped | Просмотр иллюстрации пользователем; push не поручен |
| [D-565: переход на Base UI](tasks/base-ui.md) | Завершён: миграция `4cb447a`, переходы `2c1c81d`; исправлена пагинация Vite 8, добавлены иконки и reduced motion; frontend 225/225, browser 51 passed / 3 skipped | Просмотр интерфейса пользователем; push не поручен |
| [Офлайн](tasks/offline.md) | Шифрование убрано по D-545; изоляция, блокировка выхода и сроки сохранены; frontend 236/236, browser 54 passed / 3 skipped | Следующая отдельная порция — кэш серверных данных; push не поручен |
| Playwright в Docker | Сохранён коммитом `902c1a1`; единый launcher для npm, agent_checks и CI; frontend 240/240, Python 56/56 | Полный browser suite блокирует ENOSPC в Docker/Btrfs; повторить после освобождения метаданных хранилища |
| Пагинация и зависимости | Изменения завершены; подпись убрана, пункты `N / страница`; .NET 11 RC3 и Node 26.13.1 LTS; обновлены остальные прямые npm/NuGet зависимости | После освобождения Docker-хранилища повторить интеграционные и browser проверки; push не поручен |

Запуск Playwright: [инструкция](../../web/README.md), `web/scripts/run-playwright.ts`,
`web/Dockerfile.playwright`. Node и Playwright для штатного запуска берутся из
`.node-version` и lock-файла. Порт Vite не публикуется; артефакты остаются на хосте,
поддержаны project-фильтры и test-list для rerun-failed. На хост браузерные тесты
не переключаются. Отмена runner даёт launcher время удалить свой контейнер.

Пагинация сохраняет доступное имя «Строк на странице» без видимой подписи;
существующие unit/e2e ожидания обновлены. SDK: `11.0.100-rc.3.26522.148`,
OpenApi/ApiDescription.Server/Hosting согласованы на `11.0.0-rc.3.26522.148`.
Node LTS `26.13.1` установлен через fnm, обновлены `.node-version` и Docker default.
Обновлены 11 прямых npm-зависимостей, Fido2 4.3.3, xunit.v3.mtp-v2 4.0.3
и Testcontainers.PostgreSql 4.17.0. Microsoft 11.0.0 stable не применён:
поручен именно RC3. Транзитивные NuGet-пакеты остаются под управлением
прямых зависимостей, принудительных override нет.

Проверки текущей пачки: frontend `20261118T192111-e5278f62` — success,
240/240 (Biome/TypeScript/Vitest/Vite build); Python unittest discover — 56/56.
Backend `20261118T192110-ebbccabd`: сборка всей solution без warnings/errors,
сохранён `build.binlog`; domain 529/529, API 175/175.
Интеграционные Init-тесты встретили PostgreSQL `53100: No space left on device`;
запуск остановлен через SIGINT после повторных таймаутов, итог `interrupted`.
Browser `20261118T192141-79092968` — incomplete, 0 тестов: ENOSPC при сборке
Docker-образа. Полный набор не подтверждён.

Btrfs: всё пространство распределено, Metadata DUP 2.74/3 GiB; доступные по `df`
17 GiB не устраняют ошибку распаковки. Образы/тома пользователя не удалялись.
После освобождения хранилища повторить `plan`, затем elevated `run` для
scopes `all` и `browser`. Сохранённые отчёты находятся в `.artifacts/agent-checks/`.

Проверка зависимостей: `npm ls --all` — без конфликтов, npm audit и NuGet
vulnerable с транзитивными пакетами — известных уязвимостей нет.
Outdated-отчёты в `.artifacts/dependencies/`: npm — пусто; NuGet — прямые
зависимости актуальны, кроме намеренно сохранённых Microsoft RC3.
