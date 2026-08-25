# T-FLEX Engineering Center

Инженерный центр для подготовки чертежей и базовых цен: принимает параметры
пользователя, хранит проекты и конфигурации, ставит задания в надежную
SQLite-очередь и генерирует результаты через изолированный адаптер T-FLEX.

## Структура

- `src/TFlexDrawingService.Api` - ASP.NET Core API и встроенный адаптивный интерфейс.
- `src/TFlexDrawingService.Worker` - отдельный background worker для обработки очереди.
- `src/TFlexDrawingService.Core` - доменные модели, контракты и валидация.
- `src/TFlexDrawingService.Infrastructure` - SQLite, файловое хранилище, JSON-каталог шаблонов, mock T-FLEX adapter.
- `templates/templates.json` - конфигурация шаблонов и параметров.
- `storage/` - БД, рабочие копии шаблонов и результаты генерации.

Если T-FLEX шаблон состоит из главного файла и фрагментов, главный файл кладется в `templates` как `TemplateName.grb`, а фрагменты - в соседнюю папку `templates/TemplateName/`. При обработке задания сервис копирует в `storage/jobs/{jobId}/attempt-{leaseHash}` и главный файл, и одноименную папку фрагментов. Каталог конкретной попытки не пересекается с повторным запуском того же задания.

## Запуск

Локально явно задайте окружение `Development`. API в этом окружении отключает
аутентификацию, а Worker использует переносимый `Mock`, поэтому ни одна
персональная Windows-папка в репозитории не требуется.

В одном терминале:

```bash
DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --project src/TFlexDrawingService.Api
```

Во втором терминале:

```bash
DOTNET_ENVIRONMENT=Development \
  dotnet run --project src/TFlexDrawingService.Worker
```

API обслуживает веб-форму на своем корневом URL. Worker забирает задания из SQLite-очереди, копирует исходный шаблон в изолированный каталог попытки `storage/jobs/{jobId}/attempt-{leaseHash}` и создает mock-результат в таком же каталоге под `storage/generated/{jobId}`.

### API в Docker на macOS и Linux

API имеет отдельный Linux-образ, который собирается для поддерживаемой Docker
платформы, включая `linux/amd64` и `linux/arm64`:

```bash
docker build --file Dockerfile.api --tag tflex-drawing-service-api:local .
docker volume create tflex-drawing-storage
docker run --detach \
  --name tflex-drawing-api \
  --publish 127.0.0.1:5011:8080 \
  --mount type=volume,source=tflex-drawing-storage,target=/workspace/storage \
  --env ASPNETCORE_ENVIRONMENT=Development \
  --env DOTNET_ENVIRONMENT=Development \
  --env Security__RequireAuthentication=false \
  tflex-drawing-service-api:local
```

После запуска интерфейс доступен на `http://127.0.0.1:5011`, а liveness — на
`http://127.0.0.1:5011/api/health/live`. Отключение аутентификации в этой
команде предназначено только для локальной loopback-разработки. В production
необходимо передать конфигурацию пользователей и оставить
`Security:RequireAuthentication=true`.

Каталог шаблонов включен в образ. Для сохранения шаблонов, импортированных через
административный интерфейс, смонтируйте отдельный постоянный каталог в
`/workspace/templates`.

## Production-развертывание

Рекомендуемый вариант на Windows Server 2022 запускает API из
`Dockerfile.api.windows`, а Worker/Runner/T-FLEX оставляет Windows-службой. Linux
образ `Dockerfile.api` используется на macOS/Linux и не меняет серверную схему. Полная
установка, обновление, проверка candidate-контейнера и rollback выполняются
скриптом `scripts/Deploy-TFlexHybridServer2022.ps1`.

Команда для чистой машины и повторного обновления приведена в
`docs/server-bootstrap.md`. Классическая установка API и Worker как двух
Windows-служб сохранена как аварийный fallback.

## Добавление шаблона администратором

В личном кабинете администратора откройте «Центр шаблонов» и загрузите:

1. JSON-манифест с параметрами, вычисляемыми переменными и правилами;
2. основной файл `.grb`;
3. при необходимости ZIP с одноименной папкой фрагментов.

Готовый пример манифеста доступен прямо в форме и хранится в
`src/TFlexDrawingService.Api/wwwroot/assets/template-manifest.example.json`.
Импорт проверяет расширения, размеры, дубли id/code, структуру определений,
безопасность путей ZIP и обновляет каталог атомарно. Рабочие импортированные
шаблоны сохраняются штатным Windows installer при обновлении и откате.

## Интеграция с T-FLEX CAD

По умолчанию worker использует режим `ExternalProcess`: backend создает рабочую копию шаблона, пишет `tflex-automation-request.json` и `parameters.par`, затем запускает внешний Windows runner, который должен работать на машине с установленным T-FLEX CAD и его Open API.

Настройка runner:

```json
{
  "TFlexAutomation": {
    "Mode": "ExternalProcess",
    "CommandPath": "C:\\Path\\To\\TFlexAutomationRunner.exe",
    "Arguments": "\"{requestPath}\" \"{responsePath}\"",
    "TimeoutSeconds": 600,
    "WriteParameterFile": true
  }
}
```

Поддерживаемые placeholders в `Arguments`: `{requestPath}`, `{responsePath}`, `{jobId}`, `{workingDirectory}`, `{templateCopyPath}`, `{resultDirectory}`, `{parameterFilePath}`, `{outputFormat}`.

Runner получает JSON:

```json
{
  "jobId": "job-id",
  "templateId": "template-id",
  "templateCode": "template-code",
  "workingDirectory": "storage/jobs/job-id/attempt-lease-hash",
  "templateCopyPath": "storage/jobs/job-id/attempt-lease-hash/template.grb",
  "resultDirectory": "storage/generated/job-id/attempt-lease-hash",
  "parameterFilePath": "storage/jobs/job-id/attempt-lease-hash/parameters.par",
  "outputFormat": "pdf",
  "parameters": {
    "WIDTH": 1000
  }
}
```

Runner должен открыть `templateCopyPath` через T-FLEX CAD Open API, подставить параметры, перестроить чертеж, экспортировать результат в `resultDirectory` и либо записать `responsePath`, либо просто положить файл нужного формата в `resultDirectory`.

Формат `responsePath`, если runner его пишет:

```json
{
  "files": [
    {
      "path": "result.pdf",
      "fileName": "result.pdf",
      "format": "pdf"
    }
  ]
}
```

Режим `Mock` оставлен только для локальной отладки без T-FLEX:

```json
{
  "TFlexAutomation": {
    "Mode": "Mock"
  }
}
```

Чтобы локально проверить настоящий runner, не меняйте
`appsettings.Development.json`. Передайте настройки через переменные окружения:

```powershell
$env:DOTNET_ENVIRONMENT = "Development"
$env:TFlexAutomation__Mode = "ExternalProcess"
$env:TFlexAutomation__CommandPath = "C:\Path\To\TFlexAutomationRunner.exe"
dotnet run --project src\TFlexDrawingService.Worker
```

Либо сохраните те же ключи в user secrets проекта Worker. Оба
`appsettings.Development.json` исключены из publish; Windows installer отдельно
фиксирует `Production` в окружении служб.

Production API должен быть доступен пользователю только по HTTPS. Штатная схема:
Windows NAT отображает порт контейнера как `5011:8080`, отдельное правило
Windows Firewall блокирует доступ к 5011/5012 не с loopback, а Caddy принимает
публичные `80/443`, перенаправляет HTTP на HTTPS и проксирует запросы на
`http://127.0.0.1:5011`. Публичный `http://0.0.0.0` installer отклоняет из-за
`Secure` cookie. Полная инструкция, rollback и Windows/T-FLEX CI описаны в
[`docs/server-bootstrap.md`](docs/server-bootstrap.md).
