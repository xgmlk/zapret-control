# Zapret Control

Нативное Windows-приложение на C# 5 / WinForms для установки и управления
zapret (дистрибутив Flowseal `zapret-discord-youtube`): служба, включение и
выключение, выбор стратегии, Game Filter, IPSet, фейки, обновления, диагностика
и тесты. `service.bat` для повседневной работы не нужен: его функции закрыты
вкладками приложения.

Публичная раздача — `ZapretControl.Setup.exe` из GitHub Releases. Установщик
сам скачивает совместимый payload Flowseal, проверяет SHA256, создаёт службу,
задачу синхронизации интерфейса и ярлыки. Обновления приходят по двум каналам:
payload Flowseal и само приложение (`release.json`).

## Возможности

- Служба `zapret`: установка, удаление, статус, PID `winws.exe`, запуск/остановка.
- Выбор любой стратегии `*.bat` из payload и пересоздание `ImagePath` без
  `service.bat`; `--wf-iface` сохраняется.
- Определение активного интерфейса Wi-Fi/Ethernet и нативное обновление
  `--wf-iface` в `ImagePath` и активной стратегии `.bat`.
- Вкладки: «Главная», «Служба», «Игры», «IPSet», «Фейки», «Обновления»,
  «Диагностика», «Тесты».
- Game Filter (disabled / TCP+UDP / TCP / UDP) с проверкой диапазонов.
- IPSet loaded / none / any и обновление списка.
- Замена активных фейков с SHA256 и бэкапом.
- Обновление hosts управляемым блоком и очистка кэша Discord.
- Диагностика (BFE, прокси, TCP timestamps, конфликтующие программы, hosts,
  VPN, DoH, интерфейс, задача) и нативные TCP/TLS/DPI-тесты.
- Два канала обновлений: Flowseal payload и `ZapretControl.Setup.exe`.
  Манифест `release.json`, SHA256, Authenticode, staging/backup/rollback,
  сохранение пользовательских списков, Game Filter и активных фейков.
- CLI для скриптов, задач и smoke-тестов.

## Установка

### Из публичного релиза

1. Откройте GitHub Releases проекта.
2. Скачайте `ZapretControl.Setup.exe`.
3. Запустите установщик и подтвердите UAC.
4. Выберите стратегию (по умолчанию `general.bat`), при необходимости снимите
   галочки службы/задачи/ярлыков.
5. Нажмите «Установить». Установщик скачает Flowseal 1.10.4, проверит SHA256,
   установит приложение в `%ProgramFiles%\ZapretControl`, создаст службу
   `zapret`, задачу `Zapret-IfIndex-Sync` и запустит GUI.

Требования: Windows 10/11 x64, .NET Framework 4.x, права администратора,
доступ к GitHub. На чистой машине ничего дополнительно ставить не нужно.

### Тихая установка и локальный payload

```text
ZapretControl.Setup.exe /S
ZapretControl.Setup.exe /S /payload=C:\downloads\zapret-discord-youtube-1.10.4.zip
ZapretControl.Setup.exe /S /payload=C:\zapret-discord-youtube-1.10.4
ZapretControl.Setup.exe /S /strategy="general (ALT9)"
ZapretControl.Setup.exe /S /update /wait-pid=1234
ZapretControl.Setup.exe /S /uninstall
ZapretControl.Setup.exe /S /uninstall /remove-data
ZapretControl.Setup.exe /repair
ZapretControl.Setup.exe /rollback
ZapretControl.Setup.exe --selftest
```

Параметры: `/payload=<zip|папка>`, `/payload-sha256=<hash>`, `/strategy=<имя.bat>`,
`/wait-pid=<pid>`, `/no-launch`, `/keep-data`, `/remove-data`, `/no-service`,
`/no-task`, `/no-shortcuts`. Без `/S` открывается окно установки; при нехватке
прав установщик перезапускает себя через UAC.

### Где что лежит

```text
%ProgramFiles%\ZapretControl\                 GUI, CLI, Setup
%ProgramFiles%\ZapretControl\zapret\          payload Flowseal (bin, lists, utils, *.bat)
%ProgramData%\ZapretControl\settings.json     настройки приложения
%ProgramData%\ZapretControl\logs\             setup.log, update.log, sync-iface.log, ui-error.log
%ProgramData%\ZapretControl\backups\          бэкапы фейков и hosts
%ProgramData%\ZapretControl\staging\          загрузки обновлений
%ProgramFiles%\ZapretControl\.zapret.backup-* резервные копии payload при обновлении
```

Папки приложения и payload доступны на запись только `Administrators`/`SYSTEM`,
пользователи читают и запускают. Перед переустановкой закройте GUI.

## Интерфейс

| Вкладка | Что делает |
|---|---|
| Главная | toggle службы, статус/PID, активный интерфейс, «Обновить», стратегия, версия payload, задача |
| Служба | статус, выбор стратегии, переустановка/удаление службы, регистрация задачи, проверка payload |
| Игры | Game Filter: disabled/all/tcp/udp, диапазоны портов, сохранение и применение |
| IPSet | loaded/none/any, обновление `ipset-all.txt`, резервная копия |
| Фейки | активные Discord/GameFilter, список `.bin`, замена с бэкапом |
| Обновления | проверка `release.json`, обновление и откат payload, обновление приложения, журнал |
| Диагностика | проверки окружения, hosts, кэш Discord, копирование отчёта |
| Тесты | стандартные TCP/DNS-тесты и DPI-тест HTTPS-запросов |

Статус обновляется каждые 3 секунды; долгие операции идут в фоне и не
блокируют окно.

## Обновления

`release.json` (генерируется `release.ps1`, лежит в GitHub Releases):

```json
{
  "schema": 1,
  "app": {
    "version": "1.1.0",
    "setup_url": "https://github.com/OWNER/REPO/releases/download/v1.1.0/ZapretControl.Setup.exe",
    "sha256": "...",
    "signer_thumbprint": "..."
  },
  "payload": {
    "version": "1.10.4",
    "url": "https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.10.4/zapret-discord-youtube-1.10.4.zip",
    "sha256": "..."
  },
  "min_app_version": "1.0.0"
}
```

- Приложение проверяет `release.json` по HTTPS, версии и SHA256.
- Payload ставится в staging, проверяется, служба останавливается, текущий
  payload уходит в `backup`, новый распаковывается, пользовательские списки и
  Game Filter восстанавливаются, служба пересоздаётся; при ошибке — откат.
- Обновление приложения скачивает `Setup.exe`, проверяет SHA256, а при
  наличии подписи — Authenticode (и `signer_thumbprint`, если задан),
  запускает `/S /update` и закрывает GUI. Настройки и payload сохраняются.
- Автопроверка включается на вкладке «Обновления»; автоматическая установка
  payload — отдельная галочка. Пока сертификата подписи нет, неподписанный
  Setup принимается по SHA256 без проверки издателя; невалидная подпись
  отклоняется всегда. Отдельной настройки для этого нет.
- Адрес манифеста задаётся в `settings.json` (`ManifestUrl`) и на вкладке
  «Обновления».

## Сборка

Нужен только встроенный .NET Framework 4.x; SDK/NuGet не требуются.

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Собираются:

- `dist\ZapretControl.exe` — GUI;
- `dist\ZapretControl.Cli.exe` — CLI;
- `dist\ZapretControl.Setup.exe` — установщик/обновлятор/деинсталлятор с
  встроенными GUI/CLI.

Версия берётся из `VERSION` (`x.y.z`) или `-Version`; исходники версии
генерируются в `dist\obj`. `build.ps1` проверяет, что каждый `.cs` включён в
один из target, и падает при пропуске. Сборка идёт с `/warnaserror+`.

## Подпись

Для публичной раздачи GUI, CLI и Setup должны иметь `Valid` Authenticode:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Sign -Thumbprint '<thumbprint>'
powershell -NoProfile -ExecutionPolicy Bypass -File .\sign.ps1 -VerifyOnly
```

`sign.ps1` использует `signtool.exe` из Windows SDK, SHA-256 и RFC 3161
timestamp. Сертификат и PFX-пароль не хранятся в репозитории. Без сертификата
артефакты остаются неподписанными (`NotSigned`); подпись проверяется только
при его наличии.

## Релиз

Локально:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\release.ps1
```

Скрипт собирает `dist\release.json` и `dist\SHA256SUMS.txt` по текущему
`VERSION` и `PayloadPin`. Для нового Flowseal: `-PayloadZip <zip>` (версия
берётся из `service.bat`) и при необходимости `-PayloadUrl`. Публикация — в
GitHub Actions по тегу `v*` (`.github/workflows/release.yml`): сборка,
подпись при наличии секретов `CODE_SIGN_PFX_BASE64`/`CODE_SIGN_PFX_PASSWORD`,
тесты, `release.json`, `gh release create`. Без секрета релиз создаётся как
prerelease. В GitHub Release публикуются `ZapretControl.Setup.exe`,
`release.json` и `SHA256SUMS.txt`; GUI и CLI встроены в Setup, отдельные exe
остаются только артефактами CI.

Секреты GitHub: `CODE_SIGN_PFX_BASE64` — base64 PFX, `CODE_SIGN_PFX_PASSWORD` —
пароль. Репозиторий и владелец задаются в `AppInfo.Repository` и
`release.ps1 -Repository`.

## CLI

```text
ZapretControl.Cli.exe --status
ZapretControl.Cli.exe --toggle
ZapretControl.Cli.exe --iface
ZapretControl.Cli.exe --sync-iface [--wait=120]
ZapretControl.Cli.exe --register-task | --unregister-task
ZapretControl.Cli.exe --version | --selftest | --selftest-payload
ZapretControl.Cli.exe --strategies
ZapretControl.Cli.exe --strategy-command="general (ALT9)"
ZapretControl.Cli.exe --install-service [--strategy=...] [--no-start]
ZapretControl.Cli.exe --remove-service | --restart
ZapretControl.Cli.exe --game-status
ZapretControl.Cli.exe --set-game-filter=all [--tcp=1024-65535] [--udp=1024-65535] [--apply]
ZapretControl.Cli.exe --ipset-status
ZapretControl.Cli.exe --set-ipset=loaded|none|any
ZapretControl.Cli.exe --update-ipset
ZapretControl.Cli.exe --fakes
ZapretControl.Cli.exe --replace-fake=discord|game --file=<name>
ZapretControl.Cli.exe --hosts-check | --hosts-apply
ZapretControl.Cli.exe --clear-discord-cache
ZapretControl.Cli.exe --diagnostics
ZapretControl.Cli.exe --test=standard|dpi
ZapretControl.Cli.exe --update-check
ZapretControl.Cli.exe --update-payload | --update-app | --rollback-payload
```

Вывод — `key=value`; машинные значения английские, `*_display` — русские.
Ошибки пишутся в stderr как `error=...`. Команды, меняющие службу, payload,
hosts, задачу и настройки, требуют прав администратора.

Коды `--status`: 0 Running, 1 Stopped, 2 переходное/неизвестное, 3 не
установлена, 4 нет прав, 64 неверные аргументы. `--iface`: 0 совпадает,
1 нужно обновить, 2 нет данных. `--sync-iface`: 0 успех, 1 расхождение
осталось, 3 admin, 4 не найдено, 5 access denied, 6 timeout, 7 ошибка,
64 аргументы.

## Как это работает

- `AppPaths` / `Settings` — раскладка `%ProgramFiles%`/`%ProgramData%`,
  настройки JSON, миграция и выбор payload.
- `ServiceManager` + `NativeService` + `StrategyParser` — нативные
  install/remove/status, `CreateService` через `advapi32`, разбор `.bat`,
  подстановка `%BIN%`, `%LISTS%`, `%GameFilterTCP%`, `%GameFilterUDP%`,
  проверка path traversal/reparse, запись стратегии в реестр.
- `IfaceStatus` — маршруты `0.0.0.0/0` по эффективной метрике, выбор
  Wi-Fi/Ethernet, чтение/запись `--wf-iface`, бэкап стратегии.
- `SyncTask` — задача `Zapret-IfIndex-Sync`, запускает установленный CLI от
  SYSTEM при входе.
- `PayloadManager` — версия Flowseal, SHA256, ZIP с защитой от path traversal,
  staging, backup, атомарная замена, сохранение пользовательских файлов,
  откат, обрезка старых копий.
- `IpsetManager`, `FakesManager`, `HostsManager`, `DiscordCache`,
  `Diagnostics`, `ConnectionTests` — функции `service.bat` без bat/PowerShell.
- `UpdateManifest` / `UpdateService` / `SignatureVerifier` — dual-update,
  `release.json`, SHA256, WinVerifyTrust, запуск Setup.
- `SetupEngine` / `SetupForm` / `SetupProgram` — установка, обновление,
  восстановление, удаление, UAC, ACL, ярлыки, Uninstall, журнал.
- `MainForm` и `*Tab` — окно с вкладками; `Program` — самоэлевация и короткие
  сообщения об ошибках, подробности — в `logs\ui-error.log`.

## Безопасность

- GUI и установщик работают elevated; CLI `--status`/`--iface` — без admin.
- Приложение и payload лежат в `Program Files` с ACL
  `Administrators`/`SYSTEM` FullControl, `Users` ReadAndExecute; данные — в
  `ProgramData` с теми же правами. Пользователь не может подменить elevated exe.
- `--sync-iface` пишет только `ImagePath` службы и `.bat` из корня zapret;
  имя стратегии проверяется, файл и корень — на reparse point, перед записью
  делается бэкап.
- Обновления идут только по HTTPS, с SHA256 из `release.json`; ZIP
  распаковывается с проверкой путей; payload и приложение обновляются через
  staging/backup/rollback. Authenticode проверяется для Setup, если он
  подписан; неподписанный Setup принимается по SHA256.
- `hosts` меняется только в управляемом блоке `# >>> Zapret Control` с бэкапом
  и `ipconfig /flushdns`; остальные записи сохраняются.
- Задача синхронизации регистрируется только из установленной копии в
  `Program Files`.

## Миграция со `service.bat` / `deploy.ps1`

Первый запуск Setup на машине с уже установленным zapret:

1. читает `ImagePath`, стратегию из `zapret-discord-youtube` и `--wf-iface`;
2. копирует внешний payload в `%ProgramFiles%\ZapretControl\zapret`
   (бэкапы `.bak-*` не копируются);
3. пересоздаёт службу на новый путь с сохранением стратегии и индекса
   интерфейса;
4. регистрирует задачу синхронизации и ярлыки.

Старый `sync-iface.ps1` и `redflag-fix` не используются. `deploy.ps1`
остаётся вспомогательным средством разработки и не является установщиком.

## Тесты

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-tests.ps1 -NoLive
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-tests.ps1 -RequireSignature
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-tests.ps1 -Toggle
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\t7-smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\ui-toggle.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\iface-switch.ps1 -WifiProfile "<профиль>"
```

`run-tests.ps1` проверяет версию, `--selftest`, staging/backup/сохранение
пользовательских списков (`--selftest-payload`), Setup selftest и живые
службу/интерфейс, если `zapret` установлена; `-NoLive` пропускает живые
проверки для CI. `t7-smoke.ps1` дополнительно проверяет Setup, `release.json`,
SHA256SUMS и отказ от неизвестных аргументов. `-RequireSignature` требует
`Valid` для GUI/CLI/Setup.

## Ограничения

- Артефакты пока не подписаны сертификатом Code Signing (`NotSigned`):
  неподписанный Setup принимается по SHA256, невалидная подпись отклоняется.
  Payload-обновления работают по SHA256.
- `deploy.ps1` — вспомогательное средство разработки, а не установщик.

## Файлы

```text
zapret-control/
  VERSION                       версия x.y.z
  build.ps1                     GUI + CLI + Setup
  release.ps1                   release.json + SHA256SUMS
  sign.ps1                      Authenticode
  deploy.ps1                    dev-развёртывание (не установщик)
  src/                          C# 5: служба, интерфейс, вкладки, Setup, updater
  tests/                        smoke, UI и E2E интерфейсов
  .github/workflows/            build и release CI
  dist/                         результат сборки (не исходники)
```
