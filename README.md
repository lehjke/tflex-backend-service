# T-FLEX Drawing Service MVP

MVP-сервис для приема параметров пользователя, постановки задания в SQLite-очередь и генерации результата через изолированный адаптер T-FLEX.

## Структура

- `src/TFlexDrawingService.Api` - ASP.NET Core API и простая веб-форма.
- `src/TFlexDrawingService.Worker` - отдельный background worker для обработки очереди.
- `src/TFlexDrawingService.Core` - доменные модели, контракты и валидация.
- `src/TFlexDrawingService.Infrastructure` - SQLite, файловое хранилище, JSON-каталог шаблонов, mock T-FLEX adapter.
- `templates/templates.json` - конфигурация шаблонов и параметров.
- `storage/` - БД, рабочие копии шаблонов и результаты генерации.

Если T-FLEX шаблон состоит из главного файла и фрагментов, главный файл кладется в `templates` как `TemplateName.grb`, а фрагменты - в соседнюю папку `templates/TemplateName/`. При обработке задания сервис копирует в `storage/jobs/{jobId}` и главный файл, и одноименную папку фрагментов.

## Запуск

В одном терминале:

```bash
dotnet run --project src/TFlexDrawingService.Api
```

Во втором терминале:

```bash
dotnet run --project src/TFlexDrawingService.Worker
```

API обслуживает веб-форму на своем корневом URL. Worker забирает задания из SQLite-очереди, копирует исходный шаблон в `storage/jobs/{jobId}` и создает mock-результат в `storage/generated/{jobId}`.

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
  "workingDirectory": "storage/jobs/job-id",
  "templateCopyPath": "storage/jobs/job-id/template.grb",
  "resultDirectory": "storage/generated/job-id",
  "parameterFilePath": "storage/jobs/job-id/parameters.par",
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
