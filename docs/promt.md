# Начальный промт для Codex: разработка и отладка web-приложения с T-FLEX API

Продолжай работу с проектом:

```text
/Users/lehjke/Desktop/Работа/tflex-backend-service
```

Проект уже разрабатывался с Codex. Сначала обязательно прочитай handoff-файл:

```text
docs/CHAT_HANDOFF_2026-06-30.md
```

После этого проверь фактическое состояние репозитория:

```bash
git status --short --branch
git log -8 --oneline --decorate
```

Не откатывай существующие изменения пользователя. Не выполняй `git reset --hard`, не удаляй рабочие файлы и не переписывай изменения без явного подтверждения.

## Контекст проекта

Проект — METEOR Engineering Center, внутренний web-сервис для инженерного центра. Основные задачи приложения:

1. Создание чертежей через шаблоны T-FLEX.
2. Расчет базовой заводской цены SMEC/XIZI.
3. Выпуск ТКП в Word.
4. Личный кабинет с проектами, сохраненными конфигурациями и спецификациями.
5. Административное управление пользователями и доступами.

Приложение должно помогать продавцам и инженерам самостоятельно формировать строительные задания, предварительные расчеты и документы, снижая ручную нагрузку на проектный отдел.

## Технологии

Используемый стек:

- ASP.NET Core, `net10.0`;
- статический frontend без React: HTML, CSS, JavaScript;
- SQLite;
- API: `src/TFlexDrawingService.Api`;
- Worker: `src/TFlexDrawingService.Worker`;
- Core: `src/TFlexDrawingService.Core`;
- Infrastructure: `src/TFlexDrawingService.Infrastructure`;
- Windows runner T-FLEX: `src/TFlexAutomationRunner`;
- frontend: `src/TFlexDrawingService.Api/wwwroot`;
- шаблоны: `templates/templates.json`;
- каталог цен: `src/TFlexDrawingService.Api/Data/pricing-catalog.json`.

Основные страницы:

```text
/
 /drawings
 /pricing
 /account
 /api/health
```

## Что важно сохранить

Не ломай уже реализованные функции:

- авторизация через cookie-сессии;
- роли `Admin`, `Operator`, `Viewer`;
- создание, редактирование и удаление проектов;
- сохранение конфигураций чертежей;
- очередь генерации чертежей;
- внешний T-FLEX runner;
- live preview шахт и эскалатора;
- расчет цены SMEC/XIZI;
- сохранение спецификаций;
- генерация ТКП Word;
- скачивание ТКП из сохраненных спецификаций;
- sticky-панель результата на странице `/pricing`;
- локальный шрифт Montserrat;
- русскоязычный интерфейс.

## Продуктовые правила

### Общие правила интерфейса

- Основной язык интерфейса — русский.
- В навигации должны оставаться рабочие разделы:
  - создание чертежей;
  - базовая цена;
  - личный кабинет.
- Клик по имени или аватару пользователя должен вести в личный кабинет.
- Кнопка поддержки должна открывать письмо на:

```text
DLtechsupport@meteor.ru
```

- Не использовать React, если задача явно не требует архитектурного переписывания.
- Сохранять простой статический frontend на HTML/CSS/JS.

### Правила SMEC

- SMEC — изготовитель LEHY/ELENESSA.
- Стандарт по умолчанию: `EN81-20/50:2014`.
- Не показывать поля, которых нет в исходной Excel-форме SMEC.
- Список серии/типа лифта должен позволять допустимые варианты, а не только LEHY.
- Автоматические признаки цены, например диапазон грузоподъемности или `HH > 2100`, не должны быть ручными чекбоксами. Они должны выводиться из спецификации.
- Не показывать отдельный блок «Выбранные отделки».
- Цена `0` для SMEC/LEHY/ELENESSA означает бесплатную опцию, а не ошибку.

### Правила XIZI

- Форма и группировка должны соответствовать исходному калькулятору XIZI.
- Для элементов отделки использовать изображения из `Otdelki_XIZI.xlsx`, если они доступны.
- Цена `-1` означает недоступную комбинацию. Такая комбинация должна блокировать расчет.
- Расчет XIZI считается предварительным и требует ручной проверки перед выпуском ТКП.

## Источники для сверки ценовой логики

Перед изменением ценовой логики сверяйся с исходными материалами:

```text
/Users/lehjke/Downloads/KIP.xlam
/Users/lehjke/Downloads/XIZI _calculator/
/Users/lehjke/Desktop/Dokumentatsia/Spetsifikatsia_TKP/
/Users/lehjke/Desktop/Dokumentatsia/P240174-METEOR-Версаль.xlsx
/Users/lehjke/Desktop/Dokumentatsia/Spetsifikatsia_XIZI_v_0_2_1.xlsx
/Users/lehjke/Desktop/Dokumentatsia/Otdelki_XIZI.xlsx
/Users/lehjke/Downloads/Example_sp.xlsx
```

Если формула или комбинация не подтверждается источником, не придумывай расчет. Лучше сохранить warning/manual review.

## Текущее незавершенное направление работы

Главная текущая задача — стабилизировать разработку, отладку и развертывание web-приложения с T-FLEX API.

Особенно важно проверить обновление Windows-сервера.

Серверная информация:

```text
Windows Server 2022
Репозиторий: C:\Users\Administrator\Desktop\tflex-backend-service
Установка: C:\Services\TFlexDrawingService
API: http://127.0.0.1:5011
Reverse proxy: Caddy
T-FLEX: C:\Program Files\T-FLEX CAD 17\Program
```

Наблюдаемая проблема при обновлении:

```text
Unlink of file '.git/objects/pack/pack-...idx' failed.
Should I try again? (y/n)
```

Проблема возникает во внутреннем checkout установщика:

```text
C:\Services\TFlexDrawingService\_src
```

Нельзя бесконечно отвечать `y`.

Безопасный порядок действий:

1. Прервать зависший installer через `Ctrl+C`.
2. Остановить службы API и Worker.
3. Остановить процессы Git.
4. Удалить целиком внутренний checkout `_src`, а не отдельные `.pack/.idx`.
5. Повторить установку.
6. Проверить `/api/health`.
7. Проверить работу API, Worker и Caddy.
8. Проверить генерацию одного тестового чертежа.
9. Проверить скачивание ТКП SMEC и XIZI.

Пример команд PowerShell:

```powershell
Stop-Service TFlexDrawingService.Api, TFlexDrawingService.Worker -Force -ErrorAction SilentlyContinue
Stop-Process -Name git -Force -ErrorAction SilentlyContinue

$lockedSource = "C:\Services\TFlexDrawingService\_src"
if (Test-Path $lockedSource) {
  Remove-Item $lockedSource -Recurse -Force
}

cd C:\Users\Administrator\Desktop\tflex-backend-service

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Install-TFlexDrawingService.ps1 `
  -RepositoryUrl "https://github.com/lehjke/tflex-backend-service.git" `
  -Branch "main" `
  -InstallRoot "C:\Services\TFlexDrawingService" `
  -Urls "http://127.0.0.1:5011" `
  -TFlexCadProgramDir "C:\Program Files\T-FLEX CAD 17\Program"

Restart-Service Caddy
Invoke-WebRequest "http://127.0.0.1:5011/api/health" -UseBasicParsing
```

Не удаляй:

```text
C:\Services\TFlexDrawingService\storage
```

В этой папке постоянные данные.

## Рекомендуемое улучшение installer

Проверь `scripts/Install-TFlexDrawingService.ps1`.

Нужно рассмотреть добавление режима:

```powershell
-UseExistingSource
```

Ожидаемое поведение режима:

- если передан `SourceRoot`, installer проверяет наличие solution/project files;
- если включен `-UseExistingSource`, installer полностью пропускает:
  - `git fetch`;
  - `git checkout`;
  - `git pull`;
  - clone во внутренний `_src`;
- publish выполняется напрямую из переданной папки;
- папка `storage` не очищается;
- службы API/Worker обновляются штатно;
- Caddy и URL не ломаются.

Цель — дать возможность обновлять сервер из уже проверенного локального checkout без повторной Git-операции внутри `C:\Services\TFlexDrawingService\_src`.

## Как работать с задачами

Перед изменениями:

1. Прочитай relevant files.
2. Проверь текущее состояние git.
3. Кратко опиши найденную структуру и предполагаемый план.
4. Вноси минимальные точечные изменения.
5. Не переписывай большие части проекта без необходимости.
6. После изменений запусти тесты.
7. Если менялся frontend, проверь основные страницы и responsive states.
8. В конце дай краткий отчет:
   - что изменено;
   - какие файлы затронуты;
   - какие проверки выполнены;
   - что осталось проверить вручную.

## Команды для локальной проверки

Запуск API:

```bash
dotnet run --project src/TFlexDrawingService.Api
```

Запуск Worker:

```bash
dotnet run --project src/TFlexDrawingService.Worker
```

Тесты:

```bash
dotnet test tests/TFlexDrawingService.Tests/TFlexDrawingService.Tests.csproj
```

Последнее известное состояние тестов:

```text
19/19 тестов прошли.
Есть 12 существующих nullable-предупреждений CS8604 в PricingCatalogStore.cs.
Это технический долг, а не падение тестов.
```

## Минимальный UI-checklist

После frontend-изменений проверить:

```text
/
 /drawings
 /pricing
 /account
```

Проверить:

- desktop ширину;
- mobile ширину;
- вход Admin;
- вход обычного пользователя;
- live calculation SMEC;
- live calculation XIZI;
- сохранение спецификации;
- скачивание ТКП Word;
- открытие личного кабинета по клику на имя/аватар;
- отсутствие административных функций у обычного пользователя.

## Ограничения

Нельзя:

- удалять `storage`;
- выполнять `git reset --hard`;
- откатывать изменения пользователя;
- удалять T-FLEX templates без явной причины;
- переписывать шаблоны T-FLEX ради frontend preview;
- менять ценовую логику без сверки с исходными Excel/XLA/XIZI-файлами;
- скрывать ошибки расчетов нулевой ценой;
- делать неподтвержденные формулы без warning/manual review;
- ломать текущую авторизацию, роли, проекты, конфигурации и ТКП.

## Первая задача

Начни с диагностики текущего состояния проекта.

Выполни:

```bash
git status --short --branch
git log -8 --oneline --decorate
```

Затем проверь:

```bash
dotnet test tests/TFlexDrawingService.Tests/TFlexDrawingService.Tests.csproj
```

После этого изучи:

```text
scripts/Install-TFlexDrawingService.ps1
```

И предложи/реализуй безопасное исправление installer через режим `-UseExistingSource`, чтобы можно было развертывать приложение из существующего локального checkout без повторного Git checkout/fetch/pull во внутренней папке `_src`.

При реализации обязательно:

- сохранить текущий сценарий установки через `RepositoryUrl` и `Branch`;
- добавить новый сценарий без Git-операций;
- не очищать `storage`;
- не ломать существующие параметры installer;
- добавить или обновить тесты, если в проекте уже есть тестируемая логика для installer;
- в конце дать инструкции по использованию нового режима на Windows Server.

Ожидаемый результат: installer должен позволять надежно обновлять API/Worker из существующего checkout и обходить проблему блокировки Git pack-файлов во внутренней папке `_src`.