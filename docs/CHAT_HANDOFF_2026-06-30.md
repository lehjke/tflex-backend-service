# METEOR Engineering Center: handoff для нового чата

Дата актуализации: 2026-06-30  
Рабочая папка: `/Users/lehjke/Desktop/Работа/tflex-backend-service`

## Как начать новый чат

Передайте новому чату этот файл и попросите:

> Продолжай работу с проектом `/Users/lehjke/Desktop/Работа/tflex-backend-service`. Сначала прочитай `docs/CHAT_HANDOFF_2026-06-30.md`, затем проверь `git status`, текущую ветку и фактическое состояние кода. Не откатывай существующие изменения.

## Текущее состояние Git

- Текущая ветка: `main`.
- HEAD: `b81aa01` (`new template`).
- `main` синхронизирован с `origin/main`.
- До добавления этого handoff-файла рабочее дерево было чистым.
- `feature/design` указывает на `377404b`.
- Изменения из `feature/design` уже влиты в `main` через PR #4, merge-коммит `9bbef2d`.
- Релизный тег `v2.1.1` указывает на `b5ec931`.

Перед любыми изменениями повторно выполнить:

```bash
git status --short --branch
git log -8 --oneline --decorate
```

## Назначение продукта

METEOR Engineering Center — внутренний инженерный центр для продавцов оборудования,
инженеров-проектировщиков и администраторов. Основные рабочие разделы:

1. Создание чертежей через шаблоны T-FLEX.
2. Личный кабинет с проектами и сохраненными конфигурациями.
3. Расчет заводской базовой цены SMEC/XIZI и выпуск ТКП.

Цель — позволить продавцам самостоятельно получать корректные строительные задания и
предварительные цены, снижая ручную нагрузку на проектный отдел.

## Технологии и архитектура

- ASP.NET Core, `net10.0`.
- Статический frontend без React: HTML, CSS и JavaScript в
  `src/TFlexDrawingService.Api/wwwroot`.
- SQLite хранит пользователей, сессии, проекты, конфигурации, спецификации, очередь и задания.
- API: `src/TFlexDrawingService.Api`.
- Worker: `src/TFlexDrawingService.Worker`.
- Доменные модели и валидация: `src/TFlexDrawingService.Core`.
- SQLite, очередь, файлы и автоматизация: `src/TFlexDrawingService.Infrastructure`.
- Windows runner T-FLEX: `src/TFlexAutomationRunner`.
- Описание шаблонов: `templates/templates.json`.
- Каталог цен: `src/TFlexDrawingService.Api/Data/pricing-catalog.json`.

Основные страницы:

- `/` — главная.
- `/drawings` — редактор чертежей.
- `/pricing` — расчет базовой цены.
- `/account` — личный кабинет.
- `/api/health` — health check.

## Что уже реализовано

### Чертежи

- Выбор проекта, шаблона и формата PDF/DWG/DXF.
- Динамические категории параметров и режим показа всех параметров.
- Ограничения параметров из каталога шаблонов.
- Сохранение и повторное редактирование конфигураций.
- Генерация через очередь и внешний T-FLEX runner.
- Live preview лифтовых шахт и эскалатора.
- В `b81aa01` добавлен и распарсен новый шаблон `LEHY-PRO [rear cwt]`.
- Логика страницы `/drawings` из актуального `main` сохранена при слиянии веток.

### Пользователи и проекты

- Авторизация cookie-сессией и заявка на регистрацию.
- Роли `Admin`, `Operator`, `Viewer`.
- Администратор управляет пользователями и ролями; администратор не может удалить другого
  администратора.
- Для обычных пользователей скрыты административные функции и шаблоны.
- Проекты можно создавать, редактировать и удалять.
- Поля проекта: название, адрес, номер запроса на завод.
- Администратор видит проекты и сохраненные конфигурации других пользователей.

### Расчет цены

- Два поставщика: SMEC и XIZI.
- Валюта заводской цены: CNY.
- Курс может подставляться автоматически, но пользователь может выбрать/указать его вручную.
- Расчет выполняется в live-режиме при изменении формы.
- SMEC и XIZI имеют отдельные наборы и группировки полей.
- Визуальные селекторы показывают изображение, код и описание, если данные доступны.
- Для XIZI значение цены `-1` блокирует комбинацию.
- Для SMEC/LEHY/ELENESSA цена `0` означает бесплатную опцию.
- Предварительные комбинации сопровождаются warning и требуют ручной проверки.
- Спецификация сохраняется в проект; на один лифт предполагается одна спецификация.
- Параметры цены могут заполняться из сохраненной конфигурации чертежа.
- Правая панель результата сделана sticky при прокрутке.

Основная серверная логика:

- `src/TFlexDrawingService.Api/Data/PricingCatalogStore.cs`
- `src/TFlexDrawingService.Api/Data/ProjectStore.cs`
- `src/TFlexDrawingService.Api/Program.cs`

Frontend:

- `src/TFlexDrawingService.Api/wwwroot/pricing.html`
- `src/TFlexDrawingService.Api/wwwroot/pricing.js`
- `src/TFlexDrawingService.Api/wwwroot/styles.css`

### ТКП

В коммите `377404b` реализован вывод ТКП в Word:

- генератор DOCX: `src/TFlexDrawingService.Api/Data/TkpDocxBuilder.cs`;
- endpoint: `GET /api/pricing-specifications/{specificationId}/tkp`;
- кнопка `Сохранить и вывести ТКП` сохраняет расчет и скачивает Word;
- в списке сохраненных спецификаций есть ссылка `ТКП Word`;
- в документ попадают проект, спецификация, параметры, отделка, функции, расчет и предупреждения;
- администратор может скачать ТКП спецификации другого пользователя в рамках admin scope.

Референсы, использованные для логики и структуры:

- `/Users/lehjke/Downloads/KIP.xlam` — SMEC;
- `/Users/lehjke/Downloads/XIZI _calculator/` — XIZI;
- `/Users/lehjke/Desktop/Dokumentatsia/Spetsifikatsia_TKP/` — вспомогательные материалы;
- `/Users/lehjke/Desktop/Dokumentatsia/P240174-METEOR-Версаль.xlsx`;
- `/Users/lehjke/Desktop/Dokumentatsia/Spetsifikatsia_XIZI_v_0_2_1.xlsx`;
- `/Users/lehjke/Desktop/Dokumentatsia/Otdelki_XIZI.xlsx`;
- `/Users/lehjke/Downloads/Example_sp.xlsx`.

## Подтвержденные продуктовые правила

### Общие

- Основной интерфейс на русском.
- Шрифт Montserrat хранится локально:
  `wwwroot/assets/fonts/Montserrat-Variable.ttf` и
  `wwwroot/assets/fonts/Montserrat-Italic-Variable.ttf`.
- В навигации должны оставаться рабочие продуктовые разделы: создание чертежей, личный
  кабинет, базовая цена.
- Клик по имени или аватару пользователя ведет в личный кабинет.
- Кнопка поддержки открывает письмо на `DLtechsupport@meteor.ru`.

### SMEC

- SMEC — изготовитель LEHY/ELENESSA.
- Стандарт по умолчанию: `EN81-20/50:2014`.
- Лишние поля, отсутствующие в исходной Excel-форме SMEC, не должны отображаться.
- Список серии/типа лифта должен позволять допустимые варианты, а не только LEHY.
- Автоматические ценовые признаки вроде диапазона грузоподъемности или `HH > 2100` не должны
  быть ручными чекбоксами: они выводятся из спецификации.
- Не показывать отдельный блок «Выбранные отделки».

### XIZI

- Форма и группировка должны соответствовать исходному калькулятору XIZI.
- Для элементов отделки использовать изображения из `Otdelki_XIZI.xlsx`, когда они есть.
- Недоступная комбинация с ценой `-1` должна блокировать расчет, а не давать нулевую цену.
- Расчет XIZI предварительный и требует ручной проверки перед ТКП.

## Проверка проекта локально

В двух терминалах:

```bash
dotnet run --project src/TFlexDrawingService.Api
dotnet run --project src/TFlexDrawingService.Worker
```

Тесты:

```bash
dotnet test tests/TFlexDrawingService.Tests/TFlexDrawingService.Tests.csproj
```

Последняя проверка 2026-06-30: `19/19` тестов прошли. Компилятор выводит 12 существующих
nullable-предупреждений `CS8604` в `PricingCatalogStore.cs`; это технический долг, а не падение
тестов.

Для UI-проверки использовать in-app Browser и проверить минимум:

- `/`;
- `/drawings`;
- `/pricing`;
- `/account`;
- desktop и mobile ширины;
- вход Admin и обычного пользователя;
- live calculation SMEC/XIZI;
- сохранение спецификации и скачивание ТКП.

## Текущее незавершенное: обновление Windows-сервера

Сервер:

- Windows Server 2022;
- репозиторий: `C:\Users\Administrator\Desktop\tflex-backend-service`;
- установка: `C:\Services\TFlexDrawingService`;
- API: `http://127.0.0.1:5011`;
- reverse proxy: Caddy;
- T-FLEX: `C:\Program Files\T-FLEX CAD 17\Program`.

Наблюдаемая проблема:

```text
Unlink of file '.git/objects/pack/pack-...idx' failed.
Should I try again? (y/n)
```

Проблема возникает во внутреннем checkout установщика
`C:\Services\TFlexDrawingService\_src` (в более ранних сообщениях папка называлась `Source`).
Git pack-файл заблокирован процессом или защитным ПО. Нельзя бесконечно отвечать `y`.

Дополнительная проблема локального checkout:

```text
Your configuration specifies to merge with the ref 'refs/heads/security'
from the remote, but no such ref was fetched.
```

Исправление upstream:

```powershell
cd C:\Users\Administrator\Desktop\tflex-backend-service
git branch --unset-upstream
git fetch origin
git checkout main
git branch --set-upstream-to=origin/main main
git pull --ff-only
```

Важно: параметр `-SourceRoot` не отключает Git-операции. Если в папке есть `.git`, установщик
все равно выполняет `git fetch`, `git checkout` и `git pull`. Поэтому простая передача локального
checkout через `-SourceRoot` не обходит блокировку полностью.

Безопасный следующий шаг:

1. Прервать зависший installer через `Ctrl+C`.
2. Остановить службы и процессы Git.
3. Удалить целиком внутренний checkout `_src`, а не отдельные `.pack/.idx`.
4. Повторить установку.

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

Если `_src` снова блокируется, сначала определить владельца блокировки через Resource Monitor,
Process Explorer или перезагрузить сервер. Не удалять отдельные Git pack-файлы: это может
повредить checkout.

### Рекомендуемое исправление установщика

Текущий `scripts/Install-TFlexDrawingService.ps1` всегда обновляет Git checkout. Для надежного
развертывания стоит добавить отдельный режим наподобие `-UseExistingSource`, при котором:

- `SourceRoot` проверяется на наличие solution/project files;
- `git fetch/checkout/pull` полностью пропускаются;
- publish выполняется прямо из переданной папки;
- `storage` по-прежнему не очищается.

Это устранит повторный clone/fetch и позволит обновлять сервер из уже проверенного локального
checkout. Такого изменения в коде пока нет.

## Важные ограничения при продолжении

- Не удалять `C:\Services\TFlexDrawingService\storage`: там постоянные данные.
- Не переписывать шаблоны T-FLEX ради визуального preview, если задача касается только frontend.
- Не откатывать изменения пользователя и не выполнять `git reset --hard`.
- Перед изменением ценовой логики сверяться с исходными Excel/XLA/XIZI-материалами.
- Для сложных ценовых комбинаций сохранять warning/manual review, если источник не дает
  однозначной формулы.
- После backend-изменений выполнять полный `dotnet test`.
- После frontend-изменений проверять desktop/mobile и реальные interaction states в браузере.

## Ближайший разумный порядок работы

1. Починить и проверить обновление Windows-сервера.
2. Проверить `/api/health`, службы API/Worker/Caddy и генерацию одного тестового чертежа.
3. Проверить скачивание ТКП SMEC и XIZI на сервере.
4. При необходимости добавить в installer режим `-UseExistingSource`.
5. После стабилизации развертывания продолжать уточнение формул и состава ТКП по реальным
   спецификациям.
