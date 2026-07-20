const STORAGE_KEY = "tflex-language";
const SUPPORTED_LANGUAGES = new Set(["ru", "en"]);

const ENGLISH_BY_RUSSIAN = new Map([
  ["Главная", "Home"],
  ["Инженерный центр — METEOR Engineering Center", "Engineering Center — METEOR Engineering Center"],
  ["Создание чертежей — METEOR Engineering Center", "Create drawings — METEOR Engineering Center"],
  ["Личный кабинет — METEOR Engineering Center", "Account — METEOR Engineering Center"],
  ["Базовая цена — METEOR Engineering Center", "Base price — METEOR Engineering Center"],
  ["Инженерный центр", "Engineering Center"],
  ["Создание чертежей", "Create drawings"],
  ["Личный кабинет", "Account"],
  ["Базовая цена", "Base price"],
  ["Поддержка инженера", "Engineering support"],
  ["Нужна проверка нестандартной шахты?", "Need a non-standard shaft review?"],
  ["Запросить", "Request"],
  ["Разделы", "Sections"],
  ["Навигация", "Navigation"],
  ["Открыть меню", "Open menu"],
  ["Закрыть меню", "Close menu"],
  ["Открыть личный кабинет", "Open account"],
  ["Поиск", "Search"],
  ["Поиск проекта, чертежа, шаблона", "Search projects, drawings, templates"],
  ["Поиск по проектам, адресу, номеру лифта", "Search by project, address, lift number"],
  ["Логин", "Username"],
  ["Пароль", "Password"],
  ["Имя", "Name"],
  ["Войти", "Sign in"],
  ["Выйти", "Sign out"],
  ["Вход", "Sign in"],
  ["Заявка на доступ", "Access request"],
  ["Отправить заявку", "Submit request"],
  ["Нет аккаунта?", "No account?"],
  ["Зарегистрироваться", "Register"],
  ["Уже есть аккаунт?", "Already registered?"],
  ["Инженерный центр", "Engineering Center"],
  ["Выберите раздел для работы с чертежами, расчетами и проектами", "Choose a section for drawings, estimates, and projects"],
  ["Быстрый доступ", "Quick access"],
  ["Использовать создание чертежей", "Open drawing creation"],
  ["Использовать расчет цен", "Open price calculation"],
  ["TFlex шаблоны", "TFlex templates"],
  ["Создание строительных заданий через САПР TFlex.", "Create building assignments with TFlex CAD."],
  ["активных шаблонов", "active templates"],
  ["Расчет цен", "Price calculation"],
  ["Расчет базовой цены.", "Calculate the base price."],
  ["Использовать", "Open"],
  ["Конфигуратор чертежей", "Drawing configurator"],
  ["Проект и шаблон", "Project and template"],
  ["Начать заново", "Reset"],
  ["Проект", "Project"],
  ["Шаблон", "Template"],
  ["Конфигурация", "Configuration"],
  ["Формат", "Format"],
  ["Сохранить", "Save"],
  ["Параметры чертежа", "Drawing parameters"],
  ["Показать все параметры", "Show all parameters"],
  ["Категории параметров", "Parameter categories"],
  ["Конфигурация готова к генерации PDF/DWG/DXF.", "Configuration is ready for PDF/DWG/DXF generation."],
  ["План шахты", "Shaft plan"],
  ["Live preview по текущим параметрам", "Live preview from current parameters"],
  ["Предпросмотр доступен для шаблонов LEHY-L-PRO, LEHY-PRO и K-II-TYPE.", "Preview is available for LEHY-L-PRO, LEHY-PRO, and K-II-TYPE templates."],
  ["Предпросмотр", "Preview"],
  ["Preview недоступен", "Preview unavailable"],
  ["Выберите шаблон, чтобы увидеть предпросмотр.", "Select a template to see a preview."],
  ["Недостаточно размеров AH, BH, AA и BB для построения плана.", "AH, BH, AA, and BB are required to build the plan."],
  ["Профиль эскалатора", "Escalator profile"],
  ["Динамический профиль эскалатора", "Dynamic escalator profile"],
  ["Недостаточно размеров HE, alpha, TK и TJ для построения профиля эскалатора.", "HE, alpha, TK, and TJ are required to build the escalator profile."],
  ["Динамический план шахты", "Dynamic shaft plan"],
  ["Генерация", "Generation"],
  ["Нет активного задания", "No active job"],
  ["Создать чертеж", "Create drawing"],
  ["Скачать PDF", "Download PDF"],
  ["Предпросмотр PDF", "Preview PDF"],
  ["Последние задания", "Recent jobs"],
  ["Статус", "Status"],
  ["Создано", "Created"],
  ["Файлы", "Files"],
  ["Задание", "Job"],
  ["Завершено", "Completed"],
  ["Ошибка", "Error"],
  ["Результат", "Result"],
  ["Проверьте параметры", "Check the parameters"],
  ["Параметры не проходят проверку T-FLEX.", "The parameters did not pass T-FLEX validation."],
  ["Конфигурация обновлена", "Configuration updated"],
  ["Конфигурация сохранена в проект", "Configuration saved to project"],
  ["Конфигурация загружена", "Configuration loaded"],
  ["Шаблон этой конфигурации сейчас недоступен.", "The template for this configuration is currently unavailable."],
  ["Инфо о проекте", "Project information"],
  ["Общие параметры", "General parameters"],
  ["Характеристики", "Specifications"],
  ["Параметры эскалатора", "Escalator parameters"],
  ["Противовес", "Counterweight"],
  ["Ферма", "Truss"],
  ["Опции", "Options"],
  ["Опоры", "Supports"],
  ["Приямок", "Pit"],
  ["Этаж", "Floor"],
  ["Входные площадки", "Landings"],
  ["Рамка", "Frame"],
  ["Приямок и оголовок", "Pit and overhead"],
  ["Основная надпись", "Title block"],
  ["Вертикальный разрез", "Vertical section"],
  ["Крюки", "Hooks"],
  ["Панель вызова", "Landing operating panel"],
  ["Этажный указатель", "Landing indicator"],
  ["Остановки", "Stops"],
  ["Лобби", "Lobby"],
  ["Отм.", "Level"],
  ["Пер.", "Front"],
  ["Зад.", "Rear"],
  ["скачать", "download"],
  ["Просмотреть", "Preview"],
  ["Личный кабинет", "Account"],
  ["Проекты, сохраненные конфигурации, история выпуска и доступные действия для продавца, инженера и администратора.", "Projects, saved configurations, generation history, and role-based actions."],
  ["Сводка", "Summary"],
  ["Проектов", "Projects"],
  ["Конфигурации", "Configurations"],
  ["Готовых файлов", "Ready files"],
  ["Ожидают обработки", "Pending"],
  ["Проекты", "Projects"],
  ["Название проекта", "Project name"],
  ["Адрес проекта", "Project address"],
  ["Номер запроса на завод", "Factory request number"],
  ["Название", "Name"],
  ["Адрес", "Address"],
  ["Номер запроса", "Request number"],
  ["Новый проект", "New project"],
  ["Сохраненные конфигурации", "Saved configurations"],
  ["Администратор", "Administrator"],
  ["Пользователи, шаблоны и доступность форматов.", "Users, templates, and format availability."],
  ["Открыть", "Open"],
  ["Центр шаблонов", "Template center"],
  ["Управление доступностью шаблонов, форматами генерации и пользователями.", "Manage templates, output formats, and users."],
  ["Каталог шаблонов", "Template catalog"],
  ["Форматы", "Formats"],
  ["Доступен", "Enabled"],
  ["Пользователи", "Users"],
  ["Роли", "Roles"],
  ["Действия", "Actions"],
  ["Импорт шаблона", "Import template"],
  ["Добавление новых шаблонов доступно только администраторам.", "Only administrators can add new templates."],
  ["Манифест JSON", "JSON manifest"],
  ["Файл шаблона GRB", "GRB template file"],
  ["Фрагменты ZIP (необязательно)", "Fragments ZIP (optional)"],
  ["Импортировать", "Import"],
  ["Файл манифеста", "Manifest file"],
  ["Файл шаблона", "Template file"],
  ["Архив фрагментов", "Fragments archive"],
  ["Скачать пример JSON", "Download JSON example"],
  ["Расчет базовой цены", "Base price calculation"],
  ["Проект и спецификация", "Project and specification"],
  ["Конфигурация чертежа", "Drawing configuration"],
  ["Название спецификации", "Specification name"],
  ["Валюта пересчета", "Conversion currency"],
  ["Основные параметры", "Main parameters"],
  ["Количество", "Quantity"],
  ["Грузоподъемность, кг", "Capacity, kg"],
  ["Скорость, м/с", "Speed, m/s"],
  ["Количество этажей", "Number of floors"],
  ["Количество остановок", "Number of stops"],
  ["Масса отделки", "Decoration weight"],
  ["Основной этаж", "Main floor"],
  ["Остальные этажи", "Other floors"],
  ["Силовое питание", "Power supply"],
  ["Питание освещения", "Lighting supply"],
  ["Шахта и основные размеры", "Shaft and main dimensions"],
  ["Ширина шахты (AH), мм", "Shaft width (AH), mm"],
  ["Глубина шахты (BH), мм", "Shaft depth (BH), mm"],
  ["Тип дверей шахты", "Landing door type"],
  ["Высота подъема (TR), мм", "Travel height (TR), mm"],
  ["Высота оголовка (OH), мм", "Overhead height (OH), mm"],
  ["Глубина приямка (PD), мм", "Pit depth (PD), mm"],
  ["Ширина кабины (AA), мм", "Car width (AA), mm"],
  ["Глубина кабины (BB), мм", "Car depth (BB), mm"],
  ["Высота кабины (HL), мм", "Car height (HL), mm"],
  ["Дверной режим", "Door arrangement"],
  ["Ширина дверей в чистоте (JJ), мм", "Clear door width (JJ), mm"],
  ["Высота дверей в чистоте (HH), мм", "Clear door height (HH), mm"],
  ["Идентификация и этажи", "Identification and floors"],
  ["Наименование", "Name"],
  ["Номер контракта", "Contract number"],
  ["Номер единицы", "Unit number"],
  ["№ лифта", "Lift number"],
  ["Количество лифтов", "Number of lifts"],
  ["Шахта", "Shaft"],
  ["Ширина шахты, мм", "Shaft width, mm"],
  ["Глубина шахты, мм", "Shaft depth, mm"],
  ["Высота подъема, мм", "Travel height, mm"],
  ["Тип шахты", "Shaft type"],
  ["Оголовок, мм", "Overhead, mm"],
  ["Приямок, мм", "Pit, mm"],
  ["Характеристики кабины", "Car specifications"],
  ["Ширина кабины, мм", "Car width, mm"],
  ["Глубина кабины, мм", "Car depth, mm"],
  ["Высота кабины, мм", "Car height, mm"],
  ["Тип кабины", "Car type"],
  ["Масса отделки, кг", "Decoration weight, kg"],
  ["Двери", "Doors"],
  ["Высота дверей, мм", "Door height, mm"],
  ["Огнестойкость", "Fire rating"],
  ["Тип открывания дверей", "Door opening type"],
  ["Производитель дверей", "Door manufacturer"],
  ["Тип дверей", "Door type"],
  ["Ширина дверей, мм", "Door width, mm"],
  ["Отделка", "Finishes"],
  ["Отделка кабины XIZI", "XIZI car finishes"],
  ["Дизайн по каталогу", "Catalog design"],
  ["Купе кабины", "Car walls"],
  ["Двери кабины", "Car doors"],
  ["Потолок", "Ceiling"],
  ["Пол", "Floor"],
  ["Зеркало", "Mirror"],
  ["Высота зеркала", "Mirror height"],
  ["Поручень, положение", "Handrail position"],
  ["Поручень", "Handrail"],
  ["Кнопки COP", "COP buttons"],
  ["Отделка на этажной площадке", "Landing finishes"],
  ["Основной посадочный этаж", "Main landing"],
  ["Дверь шахты, основной этаж", "Landing door, main floor"],
  ["LOP, основной этаж", "LOP, main floor"],
  ["LIP, основной этаж", "LIP, main floor"],
  ["Дверь шахты, остальные этажи", "Landing door, other floors"],
  ["LOP, остальные этажи", "LOP, other floors"],
  ["LIP, остальные этажи", "LIP, other floors"],
  ["Кабина", "Car"],
  ["Тип пола", "Floor type"],
  ["Стены кабины", "Car walls"],
  ["Положение поручня", "Handrail position"],
  ["COP для МГН", "Accessible COP"],
  ["COP 2 для МГН", "Second accessible COP"],
  ["Кнопки COP для МГН", "Accessible COP buttons"],
  ["Этажные площадки", "Landings"],
  ["Портал", "Jamb"],
  ["Материал", "Material"],
  ["Кронштейн порога", "Sill bracket"],
  ["Дверь шахты", "Landing door"],
  ["Поставщик", "Supplier"],
  ["Модель лифта", "Lift model"],
  ["Тип лифта", "Lift type"],
  ["Модель", "Model"],
  ["Серия оборудования", "Equipment series"],
  ["Количество дверей", "Number of doors"],
  ["Система управления", "Control system"],
  ["Стандарт изготовления", "Manufacturing standard"],
  ["Тип проекта", "Project type"],
  ["Дополнительный LOP", "Additional LOP"],
  ["Кнопка", "Button"],
  ["Этажный индикатор", "Hall indicator"],
  ["Этажный фонарь", "Hall lantern"],
  ["Опции и комплектация", "Options and equipment"],
  ["Прочие требования", "Other requirements"],
  ["Рассчитать", "Calculate"],
  ["Сохранить в проект", "Save to project"],
  ["Сохранить и вывести ТКП", "Save and export proposal"],
  ["Не рассчитано", "Not calculated"],
  ["Состав цены", "Price breakdown"],
  ["Заполните параметры, расчет обновится автоматически.", "Fill in the parameters; the calculation updates automatically."],
  ["Сохраненные спецификации", "Saved specifications"],
  ["Пока нет расчетов в проекте.", "No calculations in this project yet."],
  ["Считается...", "Calculating…"],
  ["Ошибка расчета", "Calculation error"],
  ["Расчет готов", "Calculation ready"],
  ["Предварительный расчет", "Preliminary calculation"],
  ["Недоступно", "Unavailable"],
  ["Курс:", "Rate:"],
  ["Контейнер", "Container"],
  ["справочно", "reference"],
  ["Ввести вручную", "Enter manually"],
  ["Создайте проект в личном кабинете", "Create a project in your account"],
  ["Создайте проект в ЛК", "Create a project in your account"],
  ["Не выбрана", "Not selected"],
  ["Не выбрано", "Not selected"],
  ["Без описания", "No description"],
  ["Дизайн кабины", "Car design"],
  ["Материал стен кабины", "Car wall material"],
  ["Материал дверей кабины", "Car door material"],
  ["Потолок кабины", "Car ceiling"],
  ["Отделка пола", "Floor finish"],
  ["Панель управления в кабине", "Car operating panel"],
  ["Материал двери шахты", "Landing door material"],
  ["Вызывной пост", "Landing operating panel"],
  ["Нет", "None"],
  ["Скачать", "Download"],
  ["Редактировать", "Edit"],
  ["Удалить", "Delete"],
  ["Сохранить проект", "Save project"],
  ["Удалить проект", "Delete project"],
  ["Пока нет проектов.", "No projects yet."],
  ["По этому запросу проекты не найдены.", "No projects match this search."],
  ["В проекте пока нет сохраненных конфигураций.", "No saved configurations in this project yet."],
  ["По этому запросу конфигурации не найдены.", "No configurations match this search."],
  ["Пока нет сохраненных конфигураций.", "No saved configurations yet."],
  ["По этому запросу задания не найдены.", "No jobs match this search."],
  ["Файл готов. Скачивание началось.", "File ready. Download started."],
  ["PDF готов к просмотру.", "PDF is ready for preview."],
  ["Задание завершено, но файл не найден.", "The job completed, but no file was found."],
  ["Генерация завершилась ошибкой.", "Generation failed."],
  ["Генерация идет дольше ожидаемого. Проверьте историю заданий в редакторе.", "Generation is taking longer than expected. Check job history in the editor."],
  ["Недостаточно прав для генерации файла.", "You do not have permission to generate files."],
  ["Недостаточно прав для создания задания.", "You do not have permission to create jobs."],
  ["Исправьте параметры перед созданием задания.", "Fix the parameters before creating the job."],
  ["Сначала создайте проект в личном кабинете.", "Create a project in your account first."],
  ["Заявка отправлена. Доступ появится после подтверждения администратором.", "Request submitted. Access will be available after administrator approval."],
  ["Неверный логин или пароль", "Incorrect username or password"],
  ["Укажите название проекта", "Enter a project name"],
  ["Закрыть", "Close"],
  ["Открыть в новой вкладке", "Open in new tab"],
  ["Скачать файл", "Download file"],
  ["Предпросмотр сформированного PDF", "Generated PDF preview"],
  ["Браузер не может показать PDF. Откройте файл в новой вкладке или скачайте его.", "This browser cannot display the PDF. Open it in a new tab or download it."],
  ["Язык", "Language"],
  ["Ничего не найдено", "No matches"],
  ["Найдено:", "Found:"],
  ["Перейти к результату", "Go to result"],
  ["Импорт выполняется...", "Importing…"],
  ["Шаблон импортирован.", "Template imported."],
  ["Выберите манифест и файл шаблона.", "Select a manifest and template file."],
  ["Не удалось импортировать шаблон", "Could not import template"],
  ["Не удалось отправить заявку", "Could not submit the request"],
  ["Ошибка создания задания", "Could not create the job"],
  ["Не удалось создать задание", "Could not create the job"],
  ["Не удалось сохранить конфигурацию", "Could not save the configuration"],
  ["Не удалось открыть конфигурацию", "Could not open the configuration"],
  ["Не удалось создать проект", "Could not create the project"],
  ["Не удалось сохранить проект", "Could not save the project"],
  ["Не удалось удалить проект", "Could not delete the project"],
  ["Не удалось удалить конфигурацию", "Could not delete the configuration"],
  ["Не удалось обновить пользователя", "Could not update the user"],
  ["Не удалось обновить шаблон", "Could not update the template"],
  ["Не удалось выполнить расчет", "Could not calculate the price"],
  ["Не удалось сохранить расчет", "Could not save the calculation"],
  ["Сохранить права", "Save permissions"],
  ["Включить", "Enable"],
  ["Подтвердить", "Approve"],
  ["Отклонить", "Reject"],
  ["Админ защищен", "Admin protected"],
  ["Нет действий", "No actions"],
  ["Обновлено", "Updated"]
]);
const RUSSIAN_BY_ENGLISH = new Map();
for (const [russian, english] of ENGLISH_BY_RUSSIAN) {
  if (!RUSSIAN_BY_ENGLISH.has(english)) {
    RUSSIAN_BY_ENGLISH.set(english, russian);
  }
}

const textOrigins = new WeakMap();
const attributeOrigins = new WeakMap();
let language = readStoredLanguage();
let observer = null;

function readStoredLanguage() {
  try {
    const value = window.localStorage.getItem(STORAGE_KEY);
    return SUPPORTED_LANGUAGES.has(value) ? value : "ru";
  } catch {
    return "ru";
  }
}

function persistLanguage(value) {
  try {
    window.localStorage.setItem(STORAGE_KEY, value);
  } catch {
    // The UI remains usable when storage is unavailable.
  }
}

function preserveWhitespace(source, translated) {
  const start = source.match(/^\s*/)?.[0] || "";
  const end = source.match(/\s*$/)?.[0] || "";
  return `${start}${translated}${end}`;
}

function translateCount(value) {
  const configurationCount = value.match(/^(\d+)\s+конф\.$/);
  if (configurationCount) return `${configurationCount[1]} configs`;

  const generationStatus = value.match(/^Генерация\s+([A-Z0-9]+):\s*(.+)$/i);
  if (generationStatus) return `Generation ${generationStatus[1]}: ${generationStatus[2]}`;

  const downloadFormat = value.match(/^Скачать\s+([A-Z0-9]+)$/i);
  if (downloadFormat) return `Download ${downloadFormat[1]}`;

  const generationFormat = value.match(/^Генерация\s+([A-Z0-9]+)\s+запущена\.\.\.$/i);
  if (generationFormat) return `${generationFormat[1]} generation started…`;

  const previewTitle = value.match(/^Предпросмотр PDF:\s+(.+)$/);
  if (previewTitle) return `Preview PDF: ${previewTitle[1]}`;

  const importedTemplate = value.match(/^Шаблон импортирован\.\s+(.+)$/);
  if (importedTemplate) return `Template imported. ${importedTemplate[1]}`;

  const searchCount = value.match(/^Найдено:\s+(\d+)$/);
  if (searchCount) return `Found: ${searchCount[1]}`;

  return value;
}

function inferRussianSource(value) {
  const exact = RUSSIAN_BY_ENGLISH.get(value);
  if (exact) return exact;

  const configurationCount = value.match(/^(\d+)\s+configs$/);
  if (configurationCount) return `${configurationCount[1]} конф.`;

  const generationStatus = value.match(/^Generation\s+([A-Z0-9]+):\s*(.+)$/i);
  if (generationStatus) return `Генерация ${generationStatus[1]}: ${generationStatus[2]}`;

  const downloadFormat = value.match(/^Download\s+([A-Z0-9]+)$/i);
  if (downloadFormat) return `Скачать ${downloadFormat[1]}`;

  const generationFormat = value.match(/^([A-Z0-9]+)\s+generation started…$/i);
  if (generationFormat) return `Генерация ${generationFormat[1]} запущена...`;

  const previewTitle = value.match(/^Preview PDF:\s+(.+)$/);
  if (previewTitle) return `Предпросмотр PDF: ${previewTitle[1]}`;

  const importedTemplate = value.match(/^Template imported\.\s+(.+)$/);
  if (importedTemplate) return `Шаблон импортирован. ${importedTemplate[1]}`;

  const searchCount = value.match(/^Found:\s+(\d+)$/);
  if (searchCount) return `Найдено: ${searchCount[1]}`;

  return value;
}

export function getLanguage() {
  return language;
}

export function t(source, params = {}) {
  let result = language === "en"
    ? (ENGLISH_BY_RUSSIAN.get(source) || translateCount(source))
    : source;

  for (const [key, value] of Object.entries(params)) {
    result = result.replaceAll(`{${key}}`, String(value));
  }

  return result;
}

function translateTextNode(node) {
  const current = node.nodeValue || "";
  const trimmed = current.trim();
  if (!trimmed) return;

  let origin = textOrigins.get(node);
  if (!origin || ENGLISH_BY_RUSSIAN.has(trimmed)) {
    const source = language === "en" ? inferRussianSource(trimmed) : trimmed;
    origin = preserveWhitespace(current, source);
    textOrigins.set(node, origin);
  }

  const source = origin.trim();
  const translated = t(source);
  const next = preserveWhitespace(origin, translated);
  if (node.nodeValue !== next) node.nodeValue = next;
}

function translateAttribute(element, attribute) {
  const current = element.getAttribute(attribute);
  if (!current) return;

  let origins = attributeOrigins.get(element);
  if (!origins) {
    origins = new Map();
    attributeOrigins.set(element, origins);
  }

  let origin = origins.get(attribute);
  if (!origin || ENGLISH_BY_RUSSIAN.has(current)) {
    origin = language === "en" ? inferRussianSource(current) : current;
    origins.set(attribute, origin);
  }

  const next = t(origin);
  if (current !== next) element.setAttribute(attribute, next);
}

function translateSubtree(root = document) {
  const rootElement = root.nodeType === Node.ELEMENT_NODE ? root : null;
  if (rootElement?.matches("script, style, noscript")) return;

  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  const nodes = [];
  while (walker.nextNode()) {
    const parent = walker.currentNode.parentElement;
    if (!parent?.closest("script, style, noscript")) nodes.push(walker.currentNode);
  }
  nodes.forEach(translateTextNode);

  const elements = [];
  if (rootElement) elements.push(rootElement);
  if (root.querySelectorAll) elements.push(...root.querySelectorAll("*"));
  for (const element of elements) {
    for (const attribute of ["aria-label", "placeholder", "title"]) {
      if (element.hasAttribute(attribute)) translateAttribute(element, attribute);
    }
  }
}

function observeDocument() {
  observer?.disconnect();
  observer = new MutationObserver(records => {
    observer.disconnect();
    for (const record of records) {
      if (record.type === "attributes") {
        translateAttribute(record.target, record.attributeName);
      } else if (record.type === "characterData") {
        translateTextNode(record.target);
      } else {
        for (const node of record.addedNodes) {
          if (node.nodeType === Node.TEXT_NODE) translateTextNode(node);
          if (node.nodeType === Node.ELEMENT_NODE) translateSubtree(node);
        }
      }
    }
    observeDocument();
  });

  observer.observe(document.documentElement, {
    subtree: true,
    childList: true,
    characterData: true,
    attributes: true,
    attributeFilter: ["aria-label", "placeholder", "title"]
  });
}

function updateLanguageControls() {
  for (const button of document.querySelectorAll("[data-language-option]")) {
    const active = button.dataset.languageOption === language;
    button.classList.toggle("is-active", active);
    button.setAttribute("aria-pressed", active ? "true" : "false");
  }
}

export function setLanguage(nextLanguage) {
  if (!SUPPORTED_LANGUAGES.has(nextLanguage)) return;
  language = nextLanguage;
  persistLanguage(language);
  observer?.disconnect();
  document.documentElement.lang = language;
  translateSubtree(document);
  updateLanguageControls();
  observeDocument();
  window.dispatchEvent(new CustomEvent("tflex:languagechange", {
    detail: { language }
  }));
}

export function mountLanguageSwitch(container) {
  if (!container || container.querySelector("[data-language-switch]")) return;

  const group = document.createElement("div");
  group.className = "language-switch";
  group.dataset.languageSwitch = "";
  group.setAttribute("role", "group");
  group.setAttribute("aria-label", t("Язык"));
  group.innerHTML = `
    <button type="button" data-language-option="ru" aria-label="Русский">RU</button>
    <button type="button" data-language-option="en" aria-label="English">EN</button>
  `;
  group.addEventListener("click", event => {
    const button = event.target.closest("[data-language-option]");
    if (button) setLanguage(button.dataset.languageOption);
  });
  container.append(group);
  updateLanguageControls();
}

document.documentElement.lang = language;
translateSubtree(document);
observeDocument();
