# Установка на Windows-сервер одной командой

Сервер должен быть Windows с установленными:

- T-FLEX CAD с рабочей лицензией и Open API;
- .NET SDK для сборки;
- .NET Runtime / Hosting Bundle для запуска;
- .NET Framework 4.7.2;
- Git.

PowerShell нужно запускать от администратора, если скрипт должен создать Windows Services и правило firewall.

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

Повторный запуск этой же команды обновит репозиторий, пересоберет publish, перезапишет API/Worker/Runner/templates и перезапустит службы. Папка `storage` не очищается, поэтому пользователи, история заданий и результаты сохраняются между обновлениями.

При первой установке скрипт выведет bootstrap-пароль для `admin`. Этот пароль применяется только если пользователя еще нет в `storage\drawings.db`; повторное обновление сервера не перезаписывает пароль и роли существующих пользователей.

Если хотите задать первый пароль явно:

```powershell
& $script `
  -InstallRoot "C:\Services\TFlexDrawingService" `
  -Urls "http://127.0.0.1:5011" `
  -AdminUser "admin" `
  -AdminPassword "change-this-password" `
  -TFlexCadProgramDir "C:\Program Files\T-FLEX CAD 17\Program"
```

Если нужен прямой HTTP без reverse proxy, можно указать `-Urls "http://0.0.0.0:80"`. Для SSL через ACME лучше оставить API на внутреннем адресе `127.0.0.1:5011`, а публичные порты `80/443` отдать Caddy.

## HTTPS через ACME без порта в браузере

Схема:

```text
https://lehjke.online -> Caddy :443 -> http://127.0.0.1:5011
http://lehjke.online  -> Caddy :80  -> HTTPS redirect / ACME challenge
```

В адресной строке порт указывать не нужно: для `http://` браузер использует `80`, для `https://` использует `443`.

Перед выпуском сертификата в Cloudflare лучше оставить запись `lehjke.online` в режиме `DNS only` / серое облако, чтобы ACME-проверка шла напрямую на сервер. После успешной проверки `https://lehjke.online/api/health` можно включить orange cloud и поставить Cloudflare SSL/TLS mode `Full (strict)`.

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

Проверка:

```powershell
Get-Service TFlexDrawingService.Api, TFlexDrawingService.Worker, Caddy
Invoke-WebRequest http://127.0.0.1:5011/api/health -UseBasicParsing
Invoke-WebRequest https://lehjke.online/api/health -UseBasicParsing
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
Invoke-WebRequest http://127.0.0.1:5011/api/health -UseBasicParsing
```

При создании задания проверяйте:

```text
C:\Services\TFlexDrawingService\storage\jobs\{jobId}
C:\Services\TFlexDrawingService\storage\generated\{jobId}
```

В рабочей папке задания должны появляться `tflex-automation-request.json`, `parameters.par`, `tflex-automation-response.json` и результат PDF/DWG/DXF.

Не удаляйте `C:\Services\TFlexDrawingService\storage` при обновлении: это постоянное состояние сервиса.
