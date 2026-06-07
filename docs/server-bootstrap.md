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
  -TFlexCadProgramDir "C:\Program Files\T-FLEX CAD 17\Program"
```

После выполнения:

- API будет доступен локально на сервере: `http://127.0.0.1:5011`;
- API будет установлен как служба `TFlexDrawingService.Api`;
- worker будет установлен как служба `TFlexDrawingService.Worker`;
- файлы будут лежать в `C:\Services\TFlexDrawingService`;
- шаблоны будут скопированы в `C:\Services\TFlexDrawingService\templates`;
- SQLite и результаты будут в `C:\Services\TFlexDrawingService\storage`.

Повторный запуск этой же команды обновит репозиторий, пересоберет publish, перезапишет API/Worker/Runner/templates и перезапустит службы.

Если нужен прямой HTTP без reverse proxy, можно указать `-Urls "http://0.0.0.0:80"`. Для SSL через ACME лучше оставить API на внутреннем адресе `127.0.0.1:5011`, а публичные порты `80/443` отдать Caddy.

## HTTPS через ACME без порта в браузере

Схема:

```text
https://lehjke.online -> Caddy :443 -> http://127.0.0.1:5011
http://lehjke.online  -> Caddy :80  -> HTTPS redirect / ACME challenge
```

В адресной строке порт указывать не нужно: для `http://` браузер использует `80`, для `https://` использует `443`.

Перед выпуском сертификата в Cloudflare лучше оставить запись `lehjke.online` в режиме `DNS only` / серое облако, чтобы ACME-проверка шла напрямую на сервер. После успешной проверки `https://lehjke.online/api/templates` можно включить orange cloud и поставить Cloudflare SSL/TLS mode `Full (strict)`.

Сначала убедитесь, что API не занимает публичный порт `80`. Если раньше сервис был установлен на `http://0.0.0.0:80`, переустановите его на внутренний порт:

```powershell
$script = "$env:TEMP\Install-TFlexDrawingService.ps1"
Invoke-WebRequest "https://raw.githubusercontent.com/lehjke/tflex-backend-service/main/scripts/Install-TFlexDrawingService.ps1" -OutFile $script
& $script `
  -RepositoryUrl "https://github.com/lehjke/tflex-backend-service.git" `
  -Branch "main" `
  -InstallRoot "C:\Services\TFlexDrawingService" `
  -Urls "http://127.0.0.1:5011" `
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
Invoke-WebRequest http://localhost:5011/api/templates -UseBasicParsing
Invoke-WebRequest https://lehjke.online/api/templates -UseBasicParsing
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
Invoke-WebRequest http://127.0.0.1:5011/api/templates -UseBasicParsing
```

При создании задания проверяйте:

```text
C:\Services\TFlexDrawingService\storage\jobs\{jobId}
C:\Services\TFlexDrawingService\storage\generated\{jobId}
```

В рабочей папке задания должны появляться `tflex-automation-request.json`, `parameters.par`, `tflex-automation-response.json` и результат PDF/DWG/DXF.
