# Текущие задачи

Ветка `design/melodytrack-v2`. PR #15/#16 объединены локально (`a12950e`,
`bed9290`); коллизия решения о Base UI разрешена с согласия пользователя:
его ID — D-565, прежние ID сохранены. Push не поручен.

| Задача | Статус | Следующее действие |
| --- | --- | --- |
| [Маскот-гитара](tasks/sign-in-mascot.md) | Завершён и сохранён отдельным коммитом: крупный ключ, кивок и пауза 650 мс; frontend 235/235, browser 51 passed / 3 skipped | Просмотр иллюстрации пользователем; push не поручен |
| [D-565: переход на Base UI](tasks/base-ui.md) | Завершён: миграция `4cb447a`, переходы `2c1c81d`; исправлена пагинация Vite 8, добавлены иконки и reduced motion; frontend 225/225, browser 51 passed / 3 skipped | Просмотр интерфейса пользователем; push не поручен |
| [Офлайн](tasks/offline.md) | Шифрование убрано по D-545; изоляция, блокировка выхода и сроки сохранены; frontend 236/236, browser 54 passed / 3 skipped | Следующая отдельная порция — кэш серверных данных; push не поручен |
| Playwright в Docker | Реализован единый launcher для npm, agent_checks и CI; frontend 240/240, Python 56/56 | Полный browser suite блокирует ENOSPC в Docker/Btrfs; повторить после освобождения метаданных хранилища |

Запуск Playwright: [инструкция](../../web/README.md), `web/scripts/run-playwright.ts`,
`web/Dockerfile.playwright`. Node и Playwright для штатного запуска берутся из
`.node-version` и lock-файла. Порт Vite не публикуется; артефакты остаются на хосте,
поддержаны project-фильтры и test-list для rerun-failed. На хост браузерные тесты
не переключаются. Отмена runner даёт launcher время удалить свой контейнер.

Проверки текущего diff: frontend `20261118T191123-49eb4906` — success,
240 passed; Python unittest discover — 56 passed. Browser
`20261118T191143-53f03193` и `20261118T191342-e64ce2bb` — incomplete,
0 тестов: Docker не смог распаковать слой node_modules (`no space left on device`).
Btrfs: всё пространство распределено, Metadata DUP 2.74/3 GiB; доступные по `df`
17 GiB не устраняют ошибку распаковки. Образы/тома пользователя не удалялись.
Коммит новой порции не поручен. После устранения внешнего блокера:
`python3 scripts/agent_checks.py plan --scope browser`, затем elevated
`python3 scripts/agent_checks.py run --scope browser`.
