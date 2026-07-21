# Установка на Windows-сервер одной командой

Сервер должен быть Windows с установленными:

- T-FLEX CAD с рабочей лицензией и Open API;
- .NET SDK для сборки;
- .NET Runtime / Hosting Bundle для запуска;
- .NET Framework 4.7.2;
- Git.

PowerShell нужно запускать от администратора, если скрипт должен создать Windows Services и правило firewall.

## Проверка загружаемых артефактов

Bootstrap не использует moving aliases `latest`, `aka.ms` или `fwlink` для
привилегированно исполняемых prerequisites. В репозитории зафиксированы
проверенные URL, версии и SHA-256:

| Артефакт | Зафиксированная версия | SHA-256 |
| --- | --- | --- |
| Microsoft-signed stable `dotnet-install.ps1`, проверен 2026-07-20 | устанавливает SDK `10.0.302` | `6585899aed55ff6ae13dbe1e8c3b878f2d00433520e7efbe250b75db948b7da9` |
| Git for Windows | `2.55.0.3` (`v2.55.0.windows.3`) | `af12577d0fdff74243a5988197aa49b957d5044edc17004f6ddf0768996f1dca` |
| Visual Studio Build Tools | `17.14.36` | `5ae95bb02bb3442441a8d891e5bb1d2975445e2e3ee16ada5bc7bd17227f1dd7` |
| .NET Framework Developer Pack | `4.7.2` | `1fa87cc7135a5360fd8b692b5118ec60963d4ce73db4a996ca62afa2b5623a6b` |
| Caddy `windows_amd64` archive | `2.11.4` | `1708333f79e274c7697285afe6d592ab39314e0b131e9ec6bea08ad27df62ebf` |

Hash `dotnet-install.ps1` относится к подписанному содержимому официального
stable endpoint Microsoft на дату проверки. Это не hash неподписанного raw-файла
тега в репозитории `dotnet/install-scripts`.

Каждая загрузка работает fail closed: до исполнения или распаковки проверяются
исходный и конечный HTTPS-host после не более чем пяти redirect-ов, затем
SHA-256. Для `dotnet-install.ps1`, Visual Studio Build Tools и .NET Framework
Developer Pack дополнительно обязательны валидная Authenticode chain и
publisher `Microsoft Corporation`. При любом несовпадении загруженный файл
удаляется, а установка прекращается.

При обновлении dependency pin меняйте URL, версию, allowlist host-ов и digest
одним проверяемым commit-ом по официальной release-записи поставщика. Нельзя
получать ожидаемый digest динамически из того же ответа или release channel,
откуда скачивается артефакт: это создало бы непроверенный TOFU.

## Первая установка или обновление

```powershell
$script = "$env:TEMP\Install-TFlexDrawingService.ps1"
Invoke-WebRequest "https://raw.githubusercontent.com/lehjke/tflex-backend-service/main/scripts/Install-TFlexDrawingService.ps1" -OutFile $script
& $script `
  -RepositoryUrl "https://github.com/lehjke/tflex-backend-service.git" `
  -Branch "main" `
  -InstallRoot "C:\Services\TFlexDrawingService" `
  -Urls "http://127.0.0.1:5011" `
  -AdminUser "admin" `
  -TFlexCadProgramDir "C:\Program Files\T-FLEX CAD 17\Program"
```

После выполнения:

- API будет доступен локально на сервере: `http://127.0.0.1:5011`;
- API будет установлен как служба `TFlexDrawingService.Api`;
- worker будет установлен как служба `TFlexDrawingService.Worker`;
- файлы будут лежать в `C:\Services\TFlexDrawingService`;
- шаблоны будут скопированы в `C:\Services\TFlexDrawingService\templates`;
- SQLite, пользователи, проекты, конфигурации, статусы шаблонов, задания и результаты будут в `C:\Services\TFlexDrawingService\storage`.

Повторный запуск этой же команды обновит репозиторий и сначала полностью соберет
API/Worker/Runner/templates во временном staging-каталоге. Работающие службы
останавливаются только после проверки ожидаемых `.exe`, конфигурации и
`templates.json`. Если замена, запуск служб или health-check завершатся ошибкой,
установщик вернет предыдущие каталоги и снова запустит ранее работавшие службы.
Папка `storage` не перемещается и не очищается, поэтому пользователи, история
заданий и результаты сохраняются между обновлениями.

До остановки служб installer также проверяет существование каждого основного
`.grb`, указанного в staged `templates.json`. Отсутствующий файл теперь
останавливает обновление до замены live-каталогов и сообщает код шаблона и путь.
JSON-файлы читаются с явной кодировкой UTF-8, чтобы Windows PowerShell 5.1 не
искажал кириллические имена шаблонов на сервере с другой системной кодовой
страницей.

Шаблоны, импортированные администратором во время работы, также считаются
постоянными данными. Перед активацией installer переносит
`templates\imported`, сохраняет соответствующие записи действующего
`templates.json` и объединяет их с новым source-каталогом. После остановки API
merge повторяется, чтобы импорт, завершившийся во время staging, не потерялся.
Конфликт `id` или `code` между новым source-шаблоном и runtime import
останавливает обновление до замены live-каталога. Полный старый каталог входит в
rollback snapshot.

При первой установке скрипт сразу выводит bootstrap-пароль для `admin`. До
запуска служб тот же пароль и его hash записываются в
`_installer-state\pending-bootstrap-admin.json`: пароль защищен Windows DPAPI
для текущей машины, а ACL разрешает доступ только `SYSTEM` и локальным
администраторам. Если установка откатится после создания пользователя в
`storage\drawings.db`, следующий запуск повторно использует и выводит ту же
учетную запись вместо генерации несовместимого пароля. State-файл удаляется
только после успешной приемки deployment. Bootstrap-конфиг не перезаписывает
пароль и роли уже существующих пользователей.

Bootstrap загружает установщик из того же `RepositoryUrl` и `Branch`, которые
переданы ему. `InstallerScriptUrl` нужен только как явное переопределение для
репозитория, URL которого нельзя преобразовать в GitHub raw URL.

`ServicePassword`, `AdminPassword` и `AdminPasswordHash` не передаются во второй
процесс PowerShell через командную строку и не выводятся в журнал команд.
Установщик также принимает их из временных переменных окружения
`TFLEX_INSTALL_SERVICE_PASSWORD`, `TFLEX_INSTALL_ADMIN_PASSWORD` и
`TFLEX_INSTALL_ADMIN_PASSWORD_HASH`, считывает один раз и удаляет до запуска
дочерних процессов.

Обе службы получают собственное окружение
`DOTNET_ENVIRONMENT=Production`, а API дополнительно
`ASPNETCORE_ENVIRONMENT=Production`. Для Worker installer также принудительно
задает `TFlexAutomation__Mode=ExternalProcess`, актуальный
`TFlexAutomation__CommandPath` и состояние health-check. Эти service-specific
значения перекрывают устаревшие machine/service overrides, включая случайный
`Mode=Mock`. Файлы
`appsettings.Development.json` исключены из publish и дополнительно запрещены
staging-проверкой installer-а. Поэтому глобальная переменная окружения сервера
не может случайно отключить аутентификацию или включить локальный Mock.

## Обновление из существующего checkout без Git-операций

Если репозиторий уже обновлен и проверен в
`C:\Users\Administrator\Desktop\tflex-backend-service`, можно полностью отключить
`git fetch`, `git checkout`, `git pull` и работу с внутренней папкой `_src`:

```powershell
cd C:\Users\Administrator\Desktop\tflex-backend-service

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Install-TFlexDrawingService.ps1 `
  -SourceRoot "C:\Users\Administrator\Desktop\tflex-backend-service" `
  -UseExistingSource `
  -InstallRoot "C:\Services\TFlexDrawingService" `
  -Urls "http://127.0.0.1:5011" `
  -TFlexCadProgramDir "C:\Program Files\T-FLEX CAD 17\Program"
```

В этом режиме `SourceRoot` обязателен. Установщик проверяет проекты API, Worker,
T-FLEX runner (если его сборка не пропущена) и `templates\templates.json`, затем
публикует файлы из указанного checkout в staging. Git для этого сценария не требуется.
Папка `C:\Services\TFlexDrawingService\storage` не удаляется и не очищается.

При указании `ServiceUser` установщик проверяет, что учетная запись существует,
добавляет ей локальное право **Log on as a service**, настраивает обе службы через
Windows Service API и проверяет сохраненное имя учетной записи. После каждой
замены каталогов installer повторно определяет фактические SID обеих служб,
даже если при обновлении `ServiceUser` не передан. API получает `Modify` на
`storage` и `templates` для атомарного admin import; Worker получает `Modify` на
`storage` и `ReadAndExecute` на шаблоны, runner и T-FLEX. Записанные ACL
проверяются по SID.
Доменная групповая политика может отменять локальное право; в таком случае ее
необходимо изменить на уровне домена.

Значение `TFlexCadProgramDir` записывается в окружение службы Worker как
`TFLEX_CAD_PROGRAM_DIR`. Поэтому runner находит `TFlexAPI.dll` и при установке
T-FLEX CAD в нестандартный каталог; изменять системный `PATH` не требуется.

После запуска Worker сам вызывает опубликованный
`TFlexAutomationRunner.exe --health-check` под фактической Windows-учетной
записью службы. Runner открывает и закрывает реальную T-FLEX Open API session;
только затем Worker публикует ready heartbeat и `/api/health/ready` может
вернуть `200`. Ошибка SDK, профиля, лицензии или инициализации прерывает
обновление и включает rollback. `-SkipRunnerBuild` не отключает эту проверку:
проверяется и заранее собранный runner. Параметр `-SkipRunnerHealthCheck`
предназначен только для диагностики: installer проверит `/api/health/live`, но
дополнительно потребует `503` от `/api/health/ready` и выведет предупреждение.

После обновления:

```powershell
Restart-Service Caddy
Get-Service TFlexDrawingService.Api, TFlexDrawingService.Worker, Caddy
Invoke-WebRequest "http://127.0.0.1:5011/api/health/ready" -UseBasicParsing
```

Если хотите задать первый пароль явно:

```powershell
& $script `
  -InstallRoot "C:\Services\TFlexDrawingService" `
  -Urls "http://127.0.0.1:5011" `
  -AdminUser "admin" `
  -AdminPassword "change-this-password" `
  -TFlexCadProgramDir "C:\Program Files\T-FLEX CAD 17\Program"
```

Публичный прямой HTTP не поддерживается: cookie аутентификации всегда имеет
флаг `Secure`, а installer отклоняет `http://` на любом не-loopback адресе.
Оставляйте API на `http://127.0.0.1:5011`, а публичные порты `80/443` отдавайте
Caddy. Прямой публичный endpoint допустим только как отдельно настроенный
Kestrel HTTPS с валидным сертификатом.

## HTTPS через ACME без порта в браузере

Схема:

```text
https://lehjke.online -> Caddy :443 -> http://127.0.0.1:5011
http://lehjke.online  -> Caddy :80  -> HTTPS redirect / ACME challenge
```

В адресной строке порт указывать не нужно: для `http://` браузер использует `80`, для `https://` использует `443`.

Перед выпуском сертификата в Cloudflare лучше оставить запись `lehjke.online` в режиме `DNS only` / серое облако, чтобы ACME-проверка шла напрямую на сервер. После успешной проверки `https://lehjke.online/api/health/ready` можно включить orange cloud и поставить Cloudflare SSL/TLS mode `Full (strict)`.

Сначала убедитесь, что API не занимает публичный порт `80`. Если раньше сервис был установлен на `http://0.0.0.0:80`, переустановите его на внутренний порт:

```powershell
$script = "$env:TEMP\Install-TFlexDrawingService.ps1"
Invoke-WebRequest "https://raw.githubusercontent.com/lehjke/tflex-backend-service/main/scripts/Install-TFlexDrawingService.ps1" -OutFile $script
& $script `
  -RepositoryUrl "https://github.com/lehjke/tflex-backend-service.git" `
  -Branch "main" `
  -InstallRoot "C:\Services\TFlexDrawingService" `
  -Urls "http://127.0.0.1:5011" `
  -AdminUser "admin" `
  -TFlexCadProgramDir "C:\Program Files\T-FLEX CAD 17\Program"
```

Затем поставьте Caddy reverse proxy:

```powershell
$caddyScript = "$env:TEMP\Install-CaddyAcmeProxy.ps1"
Invoke-WebRequest "https://raw.githubusercontent.com/lehjke/tflex-backend-service/main/scripts/Install-CaddyAcmeProxy.ps1" -OutFile $caddyScript
& $caddyScript `
  -Domain "lehjke.online" `
  -UpstreamUrl "http://127.0.0.1:5011"
```

Caddy `2.11.4` сначала загружается в staging по зафиксированному release URL.
До распаковки archive проверяются конечный HTTPS-host и repository-pinned
SHA-256 из таблицы выше. Затем binary и новый Caddyfile проверяются без остановки
действующей службы. Только после успешной валидации файлы
переключаются, служба запускается и проверяется через публичный
`https://<domain>/api/health/ready`. Ошибка публичной проверки считается ошибкой
deployment: старые binary, Caddyfile, ImagePath, startup/recovery settings и
предыдущее состояние службы восстанавливаются автоматически.

Проверка:

```powershell
Get-Service TFlexDrawingService.Api, TFlexDrawingService.Worker, Caddy
Invoke-WebRequest http://127.0.0.1:5011/api/health/ready -UseBasicParsing
Invoke-WebRequest https://lehjke.online/api/health/ready -UseBasicParsing
```

## Пользователи и роли

Пользователи хранятся в постоянной SQLite БД:

```text
C:\Services\TFlexDrawingService\storage\drawings.db
```

Роли:

- `Admin`: видит все задания, управляет пользователями и пулом шаблонов;
- `Operator`: создает задания и видит свои задания;
- `Viewer`: только смотрит доступные ему задания и скачивает результаты.

Новый пользователь может отправить заявку через форму на главной странице или в личном кабинете. До подтверждения администратором он хранится в БД со статусом `Pending`, отключен и не может войти. Администратор подтверждает, отклоняет или отключает пользователей в админ-разделе личного кабинета.

При подтверждении из веб-интерфейса обычному новому пользователю выдаются роли `Operator` и `Viewer`. Для ручного управления через API можно передать роли явно:

Создать или обновить пользователя можно через admin API после входа под admin в браузере. Для PowerShell удобнее сначала получить cookie:

```powershell
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-RestMethod "https://lehjke.online/api/auth/login" `
  -Method Post `
  -WebSession $session `
  -ContentType "application/json" `
  -Body (@{ userName = "admin"; password = "admin-password" } | ConvertTo-Json)

Invoke-RestMethod "https://lehjke.online/api/admin/users/operator" `
  -Method Put `
  -WebSession $session `
  -Headers @{ "X-TFlex-Requested-With" = "fetch" } `
  -ContentType "application/json" `
  -Body (@{
    displayName = "Operator"
    password = "operator-password"
    enabled = $true
    roles = @("Operator", "Viewer")
  } | ConvertTo-Json)
```

Подтвердить заявку:

```powershell
Invoke-RestMethod "https://lehjke.online/api/admin/users/operator/approve" `
  -Method Post `
  -WebSession $session `
  -Headers @{ "X-TFlex-Requested-With" = "fetch" } `
  -ContentType "application/json" `
  -Body (@{ roles = @("Operator", "Viewer") } | ConvertTo-Json)
```

Отклонить заявку:

```powershell
Invoke-RestMethod "https://lehjke.online/api/admin/users/operator/reject" `
  -Method Post `
  -WebSession $session `
  -Headers @{ "X-TFlex-Requested-With" = "fetch" }
```

Отключить пользователя:

```powershell
Invoke-RestMethod "https://lehjke.online/api/admin/users/operator" `
  -Method Delete `
  -WebSession $session `
  -Headers @{ "X-TFlex-Requested-With" = "fetch" }
```

Пароли в БД хранятся только как PBKDF2-SHA256 hash. Если пользователь уже есть в БД, bootstrap-конфиг при обновлении его не перезаписывает.

Если администратор отключает пользователя или отклоняет его заявку, уже выданная cookie-сессия отзывается при следующем запросе к серверу.

## Личный кабинет и шаблоны

Личный кабинет доступен по адресу:

```text
https://lehjke.online/account.html
```

В редакторе пользователь выбирает проект и сохраняет текущую конфигурацию в него. Название сохраненной конфигурации берется из названия выбранного шаблона. В личном кабинете пользователь видит список проектов; раскрытие проекта показывает сохраненные в нем конфигурации.

Конфигурация хранит шаблон, формат вывода и текущие параметры формы. Эти данные пишутся в ту же постоянную SQLite БД:

```text
C:\Services\TFlexDrawingService\storage\drawings.db
```

Обновление сервера через installer не удаляет `drawings.db`, поэтому проекты и конфигурации остаются на месте.

Для каждой конфигурации в ЛК доступны:

- `Скачать`: запускает генерацию задания из сохраненных параметров и скачивает готовый PDF/DWG/DXF;
- `Редактировать`: открывает редактор и подставляет сохраненную конфигурацию;
- `Удалить`: удаляет сохраненную конфигурацию из проекта.

В админ-разделе личного кабинета есть пул шаблонов. Администратор может временно выключить шаблон для пользователей. Выключенный шаблон:

- исчезает из списка `/api/templates` для `Operator` и `Viewer`;
- не открывается через `/api/templates/{id}` для обычных пользователей;
- не принимается при создании задания и сохранении конфигурации;
- остается видимым администратору, чтобы его можно было включить обратно.

Статусы шаблонов также хранятся в `storage\drawings.db`, а не в publish-папке приложения.

## Лимиты и очистка

Production-конфиг по умолчанию включает:

- максимум `50` активных заданий в очереди;
- максимум `5` активных заданий на пользователя;
- лимит тела запроса `1 MB`;
- rate limit на login и создание заданий;
- автоматическую очистку завершенных заданий старше `30` дней.

Эти значения можно менять параметрами installer-а:

```powershell
& $script `
  -InstallRoot "C:\Services\TFlexDrawingService" `
  -MaxActiveJobs 50 `
  -MaxActiveJobsPerUser 5 `
  -FinishedJobRetentionDays 30
```

## Запуск под отдельным пользователем

Для T-FLEX часто нужен пользователь, под которым уже запускался CAD и доступна лицензия. В этом случае передайте учетную запись:

```powershell
& $script `
  -InstallRoot "C:\Services\TFlexDrawingService" `
  -ServiceUser ".\tflex-runner"
```

Если `-ServicePassword` не указан, скрипт спросит пароль интерактивно.

Installer сохраняет перед обновлением ImagePath, service account, service
environment, startup mode и recovery settings обеих служб. При любой ошибке
после остановки он восстанавливает эти значения вместе с предыдущими файлами.
Если одновременно меняется пользователь службы с одной обычной учетной записи
на другую, передайте старый пароль через `-PreviousServicePassword`; без него
installer остановится до изменения действующих служб, потому что Windows не
позволяет извлечь старый пароль для rollback.

## Если Runner уже собран вручную

```powershell
& $script `
  -InstallRoot "C:\Services\TFlexDrawingService" `
  -SkipRunnerBuild `
  -TFlexAutomationCommandPath "C:\Services\TFlexDrawingService\Runner\TFlexAutomationRunner.exe"
```

## Проверка после установки

```powershell
Get-Service TFlexDrawingService.Api, TFlexDrawingService.Worker
Invoke-WebRequest http://127.0.0.1:5011/api/health/ready -UseBasicParsing
```

Installer не объявляет deployment успешным, пока обе службы стабильно не
остаются в состоянии `Running`, `/api/health/ready` не подтвердит каталог
шаблонов, SQLite и свежий heartbeat Worker после успешного реального
`runner --health-check` под service identity, а анонимный запрос к защищенному
`/api/projects` не вернет ожидаемый `401`. Исключение — явно диагностический
`-SkipRunnerHealthCheck`: тогда принимается только `/api/health/live`, а
`/api/health/ready` обязан остаться `503`. Перед приемкой релиза дополнительно
создайте одно контрольное задание и проверьте итоговый PDF/DWG/DXF.

При создании задания проверяйте:

```text
C:\Services\TFlexDrawingService\storage\jobs\{jobId}\attempt-{leaseHash}
C:\Services\TFlexDrawingService\storage\generated\{jobId}\attempt-{leaseHash}
```

В рабочей папке задания должны появляться `tflex-automation-request.json`, `parameters.par`, `tflex-automation-response.json` и результат PDF/DWG/DXF.

Не удаляйте `C:\Services\TFlexDrawingService\storage` при обновлении: это постоянное состояние сервиса.

## CI для Windows и T-FLEX

Обычный workflow `.github/workflows/ci.yml` выполняется на
`windows-latest`: разбирает все PowerShell-скрипты AST parser-ом, запускает
PSScriptAnalyzer, portable tests, публикует API/Worker под `win-x64` и проверяет,
что Development-конфиги не попали в артефакты.

Runner нельзя достоверно проверить на GitHub-hosted машине без лицензированного
T-FLEX. Для него предусмотрен ручной workflow
`.github/workflows/tflex-windows-integration.yml`. Подключите self-hosted Windows
runner под учетной записью с активированной лицензией и labels:

```text
self-hosted, Windows, X64, tflex-cad
```

При запуске workflow укажите каталог, содержащий `TFlexAPI.dll` и
`TFlexAPI3D.dll`. Job останавливается, если DLL отсутствуют, затем собирает
`net472` runner и вызывает `TFlexAutomationRunner.exe --health-check`, то есть
проверяет не только компиляцию, но и реальное открытие/закрытие Open API session.
