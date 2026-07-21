import { getLanguage, t } from "./i18n.js?v=20260720-ui-hardening-4";
import { isPdfFile, openGeneratedFilePreview } from "./file-preview.js?v=20260720-ui-hardening-4";
import { evaluateTFlexExpression } from "./safe-expression.js?v=20260721-validation-parity-1";
import { createSessionRequestGuard } from "./session-requests.js?v=20260720-ui-hardening-1";
import {
  calculateAutomaticStopLevel,
  clampStopCount,
  collectStopParameterValues,
  getAuthoritativeStopLevelValues,
  getMainSelectionMode,
  getStopLevelParameterName,
  getStopNameParameterName,
  getStopRowKey,
  resolveMainFloor
} from "./stop-state.js?v=20260721-validation-parity-1";
import {
  isBlockingValidationIssue,
  isValidationPassed,
  normalizeValidationSeverity,
  partitionValidationIssues
} from "./validation-state.js?v=20260721-validation-parity-1";

const state = {
  templates: [],
  selectedTemplate: null,
  parameterValues: {},
  validationFieldNames: new Set(),
  activeJobId: null,
  pollTimer: null,
  latestJob: null,
  jobs: [],
  pendingRenderFrame: null,
  pendingFocusTarget: null,
  activeParameterCategory: null,
  showAllParameters: true,
  currentUser: null,
  projects: [],
  configurations: [],
  editingConfigurationId: null
};
const sessionRequests = createSessionRequestGuard();

const guestMain = document.querySelector("#guestMain");
const appMain = document.querySelector("#appMain");
const pageSkeleton = document.querySelector("#pageSkeleton");
const loginForm = document.querySelector("#loginForm");
const loginUserName = document.querySelector("#loginUserName");
const loginPassword = document.querySelector("#loginPassword");
const guestLoginPanel = document.querySelector("#guestLoginPanel");
const registerPanel = document.querySelector("#registerPanel");
const guestLoginForm = document.querySelector("#guestLoginForm");
const showRegisterPanelButton = document.querySelector("#showRegisterPanel");
const showLoginPanelButton = document.querySelector("#showLoginPanel");
const registerForm = document.querySelector("#registerForm");
const registerUserName = document.querySelector("#registerUserName");
const registerDisplayName = document.querySelector("#registerDisplayName");
const registerPassword = document.querySelector("#registerPassword");
const registerStatus = document.querySelector("#registerStatus");
const userPanel = document.querySelector("#userPanel");
const currentUserName = document.querySelector("#currentUserName");
const currentUserRoleLabel = document.querySelector("#currentUserRoleLabel");
const adminNavLinks = document.querySelectorAll(".admin-only-nav");
const logoutButton = document.querySelector("#logoutButton");
const createTopButton = document.querySelector("#createTopButton");
const templateSelect = document.querySelector("#templateSelect");
const formatSelect = document.querySelector("#formatSelect");
const globalSearchInput = document.querySelector(".global-search input");
const parametersForm = document.querySelector("#parametersForm");
const submitButton = document.querySelector("#submitButton");
const previewResultButton = document.querySelector("#previewResultButton");
const downloadResultButton = document.querySelector("#downloadResultButton");
const statusPanel = document.querySelector("#statusPanel");
const jobsTableBody = document.querySelector("#jobsTableBody");
const validationPanel = document.querySelector("#validationPanel");
const parameterTabs = document.querySelector("#parameterTabs");
const showAllParametersToggle = document.querySelector("#showAllParametersToggle");
const parameterReadyBanner = document.querySelector("#parameterReadyBanner");
const previewPanelTitle = document.querySelector("#previewPanelTitle");
const shaftPreviewSubtitle = document.querySelector("#shaftPreviewSubtitle");
const shaftPreviewUnavailable = document.querySelector("#shaftPreviewUnavailable");
const shaftPreviewContent = document.querySelector("#shaftPreviewContent");
const shaftPreviewCanvas = document.querySelector("#shaftPreviewCanvas");
const shaftPreviewMetrics = document.querySelector("#shaftPreviewMetrics");
const projectSelect = document.querySelector("#projectSelect");
const saveConfigurationButton = document.querySelector("#saveConfigurationButton");
const configurationNamePreview = document.querySelector("#configurationNamePreview");

const STOP_CONTROL_NAMES = new Set(["main", "name", "level", "main_floor"]);
const FRONTEND_HIDDEN_PARAMETER_NAMES = new Set(["$Oboznach", "$ver"]);
const CONFIGURATION_NAME_PARAMETER_NAMES = ["$Oboznach"];
const STOP_GROUP_LABEL = "\u041e\u0441\u0442\u0430\u043d\u043e\u0432\u043a\u0438";
const STOP_LOBBY_LABEL = "\u041b\u043e\u0431\u0431\u0438";
const STOP_FLOOR_LABEL = "\u042d\u0442\u0430\u0436";
const STOP_LEVEL_LABEL = "\u041e\u0442\u043c.";
const STOP_FRONT_LABEL = "\u041f\u0435\u0440.";
const STOP_REAR_LABEL = "\u0417\u0430\u0434.";
const STOP_AO_LABEL = "AO";
const DEFAULT_PARAMETER_CATEGORY = "\u0420\u0430\u0437\u043d\u043e\u0435";
const CATEGORY_LABEL_OVERRIDES = new Map([
  ["LOP", "Панель вызова"],
  ["LIP", "Этажный указатель"]
]);
const FIELD_LABEL_OVERRIDES = new Map([
  ["AA", "Ширина кабины"],
  ["BB", "Глубина кабины"],
  ["HL", "Высота кабины (в чистоте)"],
  ["JJ", "Ширина дверей (в чистоте)"],
  ["EI", "Огнестойкость дверей"],
  ["A4", "Эксцентриситет дверей"],
  ["HH", "Высота дверей (в чистоте)"],
  ["WW", "Ширина противовеса"],
  ["WG", "Длина противовеса"],
  ["AH", "Ширина шахты"],
  ["BH", "Глубина шахты"],
  ["CB", "От оси кабины до правой стены шахты"],
  ["CJ", "Эксцентриситет дверей"],
  ["NE", "Количество входов"],
  ["TR", "Высота подъема"],
  ["A3", "Расстояние от оси кабины до стенки без противовеса"],
  ["OH", "Оголовок"],
  ["PD", "Приямок"]
]);
const CATEGORY_DISPLAY_ORDER = [
  "Инфо о проекте",
  "Общие параметры",
  "Характеристики",
  "Параметры эскалатора",
  "Кабина",
  "Двери",
  "Противовес",
  "Шахта",
  "Ферма",
  "Опции",
  "Опоры",
  "Приямок",
  "Этаж",
  "Входные площадки",
  "Рамка",
  "Приямок и оголовок",
  "Основная надпись",
  "Вертикальный разрез",
  STOP_GROUP_LABEL,
  "Крюки",
  "Панель вызова",
  "Этажный указатель",
  "Отделка"
];
const SHAFT_PREVIEW_SUPPORTED_TEMPLATE_PREFIXES = [
  "lehy_l_pro",
  "lehy_pro"
];
const ESCALATOR_PREVIEW_SUPPORTED_TEMPLATE_PREFIXES = [
  "k_ii_type"
];
const KII_ESCALATOR_PDF_BOUNDS = {
  minX: -11.34,
  minY: -67.02,
  maxX: 842.42,
  maxY: 339.94
};
const KII_ESCALATOR_PDF_PATHS = [
  "M-11.34 0.00 L144.85 0.00",
  "M144.85 0.00 L635.83 283.46",
  "M635.83 283.46 L842.41 283.46",
  "M842.41 283.46 L842.41 282.05",
  "M842.41 282.05 L831.08 282.05",
  "M831.08 282.05 L831.08 223.82",
  "M638.88 225.52 L831.08 225.52",
  "M831.08 223.82 L639.33 223.82",
  "M-11.34 0.00 L-11.34 -1.42",
  "M-11.34 -1.42 L0.00 -1.42",
  "M0.00 -1.42 L0.00 -67.01",
  "M34.02 -59.64 L148.36 -59.64",
  "M148.36 -59.64 L639.33 223.82",
  "M638.88 225.52 L147.90 -57.94",
  "M147.90 -57.94 L0.00 -57.94",
  "M0.00 -67.01 L34.02 -67.01",
  "M34.02 -67.01 L34.02 -59.64",
  "M56.81 56.47 L55.39 56.43 L53.97 56.31 L52.56 56.11 L51.17 55.83 L49.79 55.48 L48.44 55.04 L47.11 54.54 L45.81 53.96 L44.55 53.30 L43.32 52.58 L42.14 51.79 L41.01 50.94 L39.92 50.02 L38.89 49.04 L37.91 48.01 L36.99 46.92 L36.14 45.79 L35.35 44.61 L34.63 43.38 L33.97 42.12 L33.39 40.82 L32.89 39.49 L32.45 38.14 L32.10 36.76 L31.82 35.37 L31.62 33.96 L31.50 32.55 L31.46 31.12 L31.50 29.70 L31.62 28.29 L31.82 26.88 L32.10 25.49 L32.45 24.11 L32.89 22.75 L33.39 21.43 L33.97 20.13 L34.63 18.87 L35.35 17.64 L36.14 16.46 L36.99 15.32 L37.91 14.24 L38.89 13.21 L39.92 12.23 L41.01 11.31 L42.14 10.46 L43.32 9.67 L44.55 8.94 L45.81 8.29 L47.11 7.71 L48.44 7.20 L49.79 6.77 L51.17 6.42 L52.56 6.14 L53.97 5.94 L55.39 5.82 L56.81 5.78",
  "M56.81 56.47 L145.71 56.47",
  "M56.81 53.40 L55.35 53.36 L53.90 53.21 L52.46 52.98 L51.04 52.65 L49.64 52.22 L48.28 51.71 L46.95 51.11 L45.67 50.42 L44.43 49.65 L43.24 48.80 L42.12 47.88 L41.05 46.88 L40.06 45.81 L39.13 44.69 L38.28 43.50 L37.51 42.26 L36.82 40.98 L36.22 39.65 L35.71 38.29 L35.29 36.89 L34.95 35.47 L34.72 34.03 L34.57 32.58 L34.53 31.12 L34.57 29.67 L34.72 28.22 L34.95 26.78 L35.29 25.36 L35.71 23.96 L36.22 22.60 L36.82 21.27 L37.51 19.98 L38.28 18.75 L39.13 17.56 L40.06 16.43 L41.05 15.37 L42.12 14.37 L43.24 13.45 L44.43 12.60 L45.67 11.83 L46.95 11.14 L48.28 10.54 L49.64 10.03 L51.04 9.60 L52.46 9.27 L53.90 9.03 L55.35 8.89 L56.81 8.84",
  "M68.26 5.78 L56.81 5.78",
  "M68.26 17.01 L126.65 17.01",
  "M68.26 13.95 L127.47 13.95",
  "M712.64 300.47 L672.32 300.47",
  "M672.32 300.47 L668.41 300.41 L664.51 300.22 L660.62 299.90 L656.74 299.45 L652.88 298.88 L649.03 298.18 L645.22 297.35 L641.43 296.41 L637.67 295.33 L633.96 294.14 L630.28 292.82 L626.65 291.39 L623.06 289.84 L619.53 288.17 L612.65 284.48",
  "M712.64 297.41 L673.14 297.41",
  "M673.14 297.41 L669.23 297.35 L665.33 297.16 L661.44 296.84 L657.56 296.39 L653.70 295.82 L649.85 295.12 L646.04 294.29 L642.25 293.34 L638.49 292.27 L634.78 291.08 L631.10 289.76 L627.47 288.33 L623.88 286.77 L620.35 285.10 L613.47 281.42",
  "M56.81 53.40 L146.53 53.40",
  "M146.53 53.40 L149.55 53.46 L152.57 53.62 L155.59 53.89 L158.59 54.27 L161.58 54.75 L164.54 55.34 L167.49 56.04 L170.41 56.84 L173.29 57.74 L176.15 58.75 L178.96 59.86 L181.74 61.06 L184.47 62.37 L188.91 64.76",
  "M188.91 64.76 L632.52 320.88",
  "M631.70 323.94 L188.08 67.82",
  "M145.71 56.47 L148.73 56.52 L151.75 56.68 L154.77 56.95 L157.77 57.33 L160.76 57.81 L163.72 58.40 L166.67 59.10 L169.59 59.90 L172.47 60.80 L175.33 61.81 L178.14 62.92 L180.92 64.13 L183.65 65.43 L188.08 67.82",
  "M692.19 336.87 L688.28 336.81 L684.38 336.61 L680.49 336.29 L676.61 335.85 L672.75 335.28 L668.91 334.58 L665.09 333.75 L661.30 332.80 L657.55 331.73 L653.83 330.54 L650.15 329.22 L646.52 327.79 L642.94 326.23 L639.41 324.56 L632.52 320.88",
  "M691.37 339.93 L687.46 339.87 L683.56 339.68 L679.67 339.36 L675.79 338.91 L671.93 338.34 L668.09 337.64 L664.27 336.81 L660.48 335.86 L656.73 334.79 L653.01 333.60 L649.33 332.28 L645.70 330.85 L642.12 329.29 L638.59 327.62 L635.11 325.84 L631.70 323.94",
  "M613.47 281.42 L169.85 25.30",
  "M127.47 13.95 L130.50 14.00 L133.52 14.16 L136.53 14.43 L139.54 14.81 L142.52 15.29 L145.49 15.88 L148.43 16.58 L151.35 17.38 L154.24 18.28 L157.09 19.29 L159.91 20.40 L162.68 21.61 L165.41 22.91 L169.85 25.30",
  "M612.65 284.48 L169.03 28.36",
  "M126.65 17.01 L129.68 17.06 L132.70 17.22 L135.71 17.49 L138.72 17.87 L141.70 18.35 L144.67 18.94 L147.61 19.64 L150.53 20.44 L153.42 21.35 L156.27 22.35 L159.09 23.46 L161.86 24.67 L164.59 25.97 L169.03 28.36",
  "M724.10 292.31 L725.55 292.36 L727.00 292.50 L728.44 292.74 L729.86 293.07 L731.26 293.49 L732.62 294.00 L733.95 294.61 L735.24 295.29 L736.47 296.06 L737.66 296.91 L738.79 297.84 L739.85 298.83 L740.85 299.90 L741.77 301.03 L742.62 302.21 L743.39 303.45 L744.08 304.73 L744.68 306.06 L745.19 307.43 L745.62 308.82 L745.95 310.24 L746.19 311.68 L746.33 313.13 L746.38 314.59 L746.33 316.05 L746.19 317.50 L745.95 318.94 L745.62 320.36 L745.19 321.75 L744.68 323.12 L744.08 324.44 L743.39 325.73 L742.62 326.97 L741.77 328.15 L740.85 329.28 L739.85 330.34 L738.79 331.34 L737.66 332.27 L736.47 333.11 L735.24 333.88 L733.95 334.57 L732.62 335.17 L731.26 335.69 L729.86 336.11 L728.44 336.44 L727.00 336.68 L725.55 336.82 L724.10 336.87",
  "M724.10 336.87 L692.19 336.87",
  "M724.10 289.25 L725.52 289.29 L726.93 289.41 L728.34 289.61 L729.74 289.88 L731.11 290.24 L732.47 290.67 L733.79 291.18 L735.09 291.76 L736.35 292.41 L737.58 293.13 L738.76 293.92 L739.90 294.78 L740.98 295.69 L742.02 296.67 L742.99 297.70 L743.91 298.79 L744.76 299.92 L745.55 301.11 L746.28 302.33 L746.93 303.59 L747.51 304.89 L748.02 306.22 L748.45 307.57 L748.80 308.95 L749.08 310.34 L749.28 311.75 L749.40 313.17 L749.44 314.59 L749.40 316.01 L749.28 317.43 L749.08 318.83 L748.80 320.23 L748.45 321.60 L748.02 322.96 L747.51 324.29 L746.93 325.58 L746.28 326.85 L745.55 328.07 L744.76 329.25 L743.91 330.39 L742.99 331.48 L742.02 332.51 L740.98 333.48 L739.90 334.40 L738.76 335.26 L737.58 336.05 L736.35 336.77 L735.09 337.42 L733.79 338.00 L732.47 338.51 L731.11 338.94 L729.74 339.30 L728.34 339.57 L726.93 339.77 L725.52 339.89 L724.10 339.93",
  "M724.10 339.93 L691.37 339.93",
  "M724.10 289.25 L712.64 289.25",
  "M56.81 8.84 L68.26 8.84",
  "M712.64 292.31 L724.10 292.31",
  "M145.71 141.22 L172.00 69.04",
  "M146.53 138.16 L150.76 61.46",
  "M127.47 98.70 L132.00 22.02",
  "M691.37 220.59 L651.11 324.46",
  "M692.19 217.53 L684.33 328.66",
  "M831.08 270.71 L831.43 270.71",
  "M831.43 270.71 L831.57 270.72 L831.71 270.74 L831.84 270.79 L831.96 270.85 L832.08 270.93 L832.18 271.02 L832.27 271.12 L832.35 271.24 L832.41 271.36 L832.46 271.50 L832.48 271.63 L832.49 271.77",
  "M832.49 271.77 L832.49 279.21",
  "M833.91 280.63 L833.73 280.62 L833.54 280.58 L833.37 280.52 L833.20 280.44 L833.05 280.34 L832.91 280.21 L832.79 280.08 L832.68 279.92 L832.60 279.75 L832.54 279.58 L832.51 279.40 L832.49 279.21",
  "M833.91 280.63 L841.35 280.63",
  "M841.35 280.63 L841.49 280.64 L841.63 280.67 L841.76 280.71 L841.88 280.77 L842.00 280.85 L842.10 280.94 L842.19 281.05 L842.27 281.16 L842.33 281.29 L842.38 281.42 L842.41 281.55 L842.41 281.69",
  "M842.41 281.69 L842.41 282.05",
  "M842.41 282.05 L831.08 282.05",
  "M831.08 282.05 L831.08 270.71",
  "M-11.34 -1.42 L-11.34 -1.77",
  "M-11.34 -1.77 L-11.33 -1.91 L-11.30 -2.05 L-11.26 -2.18 L-11.20 -2.30 L-11.12 -2.42 L-11.03 -2.52 L-10.92 -2.61 L-10.81 -2.69 L-10.68 -2.75 L-10.55 -2.80 L-10.28 -2.83",
  "M-10.28 -2.83 L-2.83 -2.83",
  "M-1.42 -4.25 L-1.43 -4.07 L-1.47 -3.89 L-1.53 -3.71 L-1.61 -3.54 L-1.71 -3.39 L-1.83 -3.25 L-1.97 -3.13 L-2.13 -3.02 L-2.29 -2.94 L-2.47 -2.88 L-2.65 -2.85 L-2.83 -2.83",
  "M-1.42 -4.25 L-1.42 -11.69",
  "M-1.42 -11.69 L-1.41 -11.83 L-1.38 -11.97 L-1.34 -12.10 L-1.27 -12.22 L-1.20 -12.34 L-1.11 -12.44 L-1.00 -12.54 L-0.89 -12.61 L-0.76 -12.67 L-0.63 -12.72 L-0.49 -12.75 L-0.35 -12.76",
  "M-0.35 -12.76 L0.00 -12.76",
  "M0.00 -12.76 L0.00 -1.42",
  "M0.00 -1.42 L-11.34 -1.42",
  "M68.26 17.01 L68.26 0.00",
  "M68.26 0.00 L58.68 0.00",
  "M68.26 17.01 L60.25 16.94 L50.40 16.67",
  "M50.40 16.67 L50.18 16.65 L49.96 16.61 L49.75 16.54 L49.55 16.44 L49.36 16.32 L49.20 16.17 L49.05 16.00 L48.93 15.82 L48.83 15.62 L48.76 15.41 L48.71 15.19 L48.70 14.97",
  "M48.70 14.97 L48.70 13.88",
  "M48.70 13.88 L48.71 13.74 L48.74 13.59 L48.79 13.45 L48.85 13.32 L48.93 13.19 L49.03 13.08 L49.23 12.93",
  "M49.23 12.93 L50.89 11.92 L52.61 11.00 L54.37 10.17 L56.17 9.42 L58.00 8.76 L59.87 8.19 L61.75 7.72 L63.66 7.33 L65.59 7.04 L68.26 6.80",
  "M60.73 7.96 L58.68 0.00",
  "M56.34 9.35 L56.35 9.35",
  "M56.35 9.35 L56.19 9.34 L56.03 9.29 L55.88 9.21 L55.75 9.11 L55.65 8.98 L55.57 8.83 L55.52 8.67 L55.50 8.50",
  "M55.50 8.50 L55.50 6.24",
  "M55.50 6.24 L55.52 6.07 L55.57 5.91 L55.65 5.76 L55.75 5.63 L55.88 5.53 L56.03 5.45 L56.19 5.40 L56.35 5.39",
  "M56.35 5.39 L59.91 4.76",
  "M712.64 300.47 L712.64 283.46",
  "M712.64 283.46 L722.23 283.46",
  "M730.50 300.13 L722.50 300.37 L712.64 300.47",
  "M732.20 298.43 L732.19 298.65 L732.15 298.87 L732.07 299.08 L731.98 299.28 L731.85 299.47 L731.71 299.63 L731.54 299.78 L731.35 299.90 L731.15 300.00 L730.94 300.07 L730.72 300.12 L730.50 300.13",
  "M732.20 298.43 L732.20 297.35",
  "M731.68 296.39 L731.80 296.48 L731.90 296.58 L732.00 296.70 L732.07 296.82 L732.13 296.96 L732.18 297.10 L732.20 297.35",
  "M712.64 290.27 L714.59 290.43 L716.52 290.68 L718.43 291.03 L720.33 291.47 L722.20 292.00 L724.05 292.63 L725.86 293.34 L727.64 294.14 L729.37 295.03 L731.68 296.39",
  "M720.17 291.43 L722.23 283.46",
  "M724.57 292.82 L724.55 292.82",
  "M725.40 291.97 L725.38 292.13 L725.34 292.29 L725.26 292.44 L725.15 292.57 L725.02 292.68 L724.88 292.75 L724.72 292.80 L724.55 292.82",
  "M725.40 291.97 L725.40 289.70",
  "M724.55 288.85 L724.72 288.87 L724.88 288.92 L725.02 288.99 L725.15 289.10 L725.26 289.23 L725.34 289.38 L725.38 289.53 L725.40 289.70",
  "M724.55 288.85 L721.00 288.22"
];
const KII_ESCALATOR_REFERENCE = {
  rise: 3900,
  totalRun: 11633,
  lowerLanding: 2178,
  upperLanding: 2435,
  pitLength: 4253,
  pitDepth: 1110,
  pdfRise: 283.46,
  pdfRun: 842.41
};
const KII_ESCALATOR_EXCLUDED_PDF_PATHS = new Set([
  "M145.71 141.22 L172.00 69.04",
  "M146.53 138.16 L150.76 61.46",
  "M127.47 98.70 L132.00 22.02",
  "M691.37 220.59 L651.11 324.46",
  "M692.19 217.53 L684.33 328.66"
]);

function isAuthenticated() {
  return Boolean(state.currentUser?.isAuthenticated);
}

function canCreateJobs() {
  const roles = state.currentUser?.roles || [];
  return roles.includes("Admin") || roles.includes("Operator");
}

function canAdmin() {
  return (state.currentUser?.roles || []).includes("Admin");
}

function getTemplateLabel(templateId) {
  const template = state.templates.find(item => item.id === templateId || item.code === templateId);
  return template ? (template.name || template.code || template.id) : templateId;
}

function getConfigurationName(parameters) {
  for (const name of CONFIGURATION_NAME_PARAMETER_NAMES) {
    const value = parameters?.[name] ?? getParameterValueByName(name);
    if (hasValue(value) && String(value).trim()) return String(value).trim();
  }

  const titleParameter = state.selectedTemplate?.parameters
    ?.find(parameter => (parameter.displayName || "").includes("№"));
  if (titleParameter) {
    const value = parameters?.[titleParameter.name] ?? getParameterValue(titleParameter);
    if (hasValue(value) && String(value).trim()) return String(value).trim();
  }

  return state.selectedTemplate?.name || state.selectedTemplate?.code || state.selectedTemplate?.id || "Конфигурация";
}

function getConfigurationNameParameter() {
  for (const name of CONFIGURATION_NAME_PARAMETER_NAMES) {
    const parameter = getParameterDefinition(name);
    if (parameter) return parameter;
  }

  return null;
}

function rememberConfigurationNameValue() {
  if (!configurationNamePreview || !state.selectedTemplate) return;
  const parameter = getConfigurationNameParameter();
  if (!parameter) return;

  state.parameterValues[parameter.name] = configurationNamePreview.value;
}

function updateConfigurationNamePreview(parameters = state.parameterValues) {
  if (!configurationNamePreview) return;
  if (!state.selectedTemplate) {
    configurationNamePreview.disabled = true;
    configurationNamePreview.value = "-";
    return;
  }

  configurationNamePreview.disabled = false;
  configurationNamePreview.value = getConfigurationName(parameters);
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;")
    .replaceAll("'", "&#39;");
}

function hidePageSkeleton() {
  if (pageSkeleton) {
    pageSkeleton.hidden = true;
  }
}

function updateAuthView() {
  const authenticated = isAuthenticated();
  const isAdmin = authenticated && canAdmin();
  guestMain.hidden = authenticated;
  loginForm.hidden = true;
  userPanel.hidden = !authenticated;
  appMain.hidden = !authenticated;
  createTopButton.hidden = !authenticated || !canCreateJobs();
  submitButton.hidden = authenticated && !canCreateJobs();
  saveConfigurationButton.hidden = !authenticated || !canCreateJobs();
  adminNavLinks.forEach(link => {
    link.hidden = !isAdmin;
  });

  if (authenticated) {
    currentUserName.textContent = state.currentUser.displayName || state.currentUser.userName;
    if (currentUserRoleLabel) {
      currentUserRoleLabel.hidden = !isAdmin;
      currentUserRoleLabel.textContent = isAdmin ? "Admin" : "";
    }
  } else {
    currentUserName.textContent = "";
    if (currentUserRoleLabel) {
      currentUserRoleLabel.hidden = true;
      currentUserRoleLabel.textContent = "";
    }
  }
}

function clearEditorSessionState() {
  sessionRequests.invalidate();
  if (state.pendingRenderFrame !== null) {
    cancelAnimationFrame(state.pendingRenderFrame);
  }
  clearInterval(state.pollTimer);

  state.templates = [];
  state.selectedTemplate = null;
  state.parameterValues = {};
  state.validationFieldNames = new Set();
  state.activeJobId = null;
  state.pollTimer = null;
  state.latestJob = null;
  state.jobs = [];
  state.pendingRenderFrame = null;
  state.pendingFocusTarget = null;
  state.activeParameterCategory = null;
  state.projects = [];
  state.configurations = [];
  state.editingConfigurationId = null;

  loginForm?.reset();
  guestLoginForm?.reset();
  registerForm?.reset();
  document.querySelector("#jobForm")?.reset();
  loginPassword?.setCustomValidity("");
  guestLoginForm?.querySelector("[name='password']")?.setCustomValidity("");
  if (registerStatus) {
    registerStatus.hidden = true;
    registerStatus.textContent = "";
  }
  if (globalSearchInput) globalSearchInput.value = "";

  jobsTableBody.replaceChildren();
  templateSelect.replaceChildren();
  formatSelect.replaceChildren();
  projectSelect.replaceChildren();
  parametersForm.replaceChildren();
  parameterTabs?.replaceChildren();
  updateValidationPanel();
  updateConfigurationNamePreview();
  updateDownloadResultButton(null);
  if (parameterReadyBanner) parameterReadyBanner.hidden = true;
  statusPanel.className = "empty";
  statusPanel.textContent = "";
  updateShaftPreview(null);

  const currentUrl = new URL(window.location.href);
  if (currentUrl.searchParams.has("configurationId")) {
    currentUrl.searchParams.delete("configurationId");
    window.history.replaceState(null, "", currentUrl);
  }
}

function showAuthPanel(panel) {
  const showRegister = panel === "register";
  if (guestLoginPanel) guestLoginPanel.hidden = showRegister;
  if (registerPanel) registerPanel.hidden = !showRegister;

  requestAnimationFrame(() => {
    const target = showRegister ? registerUserName : guestLoginForm?.querySelector("[name='userName']");
    target?.focus({ preventScroll: true });
  });
}

async function apiFetch(url, options = {}) {
  const method = (options.method || "GET").toUpperCase();
  const headers = new Headers(options.headers || {});
  if (method !== "GET" && method !== "HEAD") {
    headers.set("X-TFlex-Requested-With", "fetch");
  }

  const response = await sessionRequests.fetch(url, {
    credentials: "same-origin",
    ...options,
    headers
  });

  if (sessionRequests.isCurrent(response) && response.status === 401) {
    clearEditorSessionState();
    state.currentUser = null;
    updateAuthView();
  }

  return response;
}

async function readProblem(response, fallback) {
  if (!sessionRequests.isCurrent(response)) return [];

  try {
    const problem = await sessionRequests.readJson(response);
    if (problem === sessionRequests.stalePayload) return [];
    return problem.errors?.request || [problem.detail || problem.title || fallback];
  } catch {
    return [fallback];
  }
}

function formatDate(value) {
  if (!value) return "";
  return new Intl.DateTimeFormat(getLanguage() === "en" ? "en-GB" : "ru-RU", {
    dateStyle: "short",
    timeStyle: "medium"
  }).format(new Date(value));
}

function normalizeSearch(value) {
  return String(value || "").trim().toLowerCase();
}

function getResultFileFormat(file) {
  const format = String(file?.format || "").trim().replace(/^\./, "");
  if (format) return format.toUpperCase();

  const extension = String(file?.fileName || "").split(".").pop();
  return extension ? extension.toUpperCase() : "";
}

function getPreferredResultFile(files = []) {
  if (!files.length) return null;

  const currentFormat = String(formatSelect?.value || "").trim().toUpperCase();
  return files.find(file => getResultFileFormat(file) === "PDF")
    || files.find(file => currentFormat && getResultFileFormat(file) === currentFormat)
    || files[0];
}

function updateDownloadResultButton(job = undefined) {
  if (!downloadResultButton) return;

  const sourceJob = job === undefined ? state.latestJob : job;
  const file = getPreferredResultFile(sourceJob?.resultFiles || []);
  if (!file?.downloadUrl) {
    const expectedFormat = String(formatSelect?.value || "pdf").toUpperCase();
    downloadResultButton.disabled = true;
    downloadResultButton.dataset.downloadUrl = "";
    downloadResultButton.textContent = `Скачать ${expectedFormat}`;
    if (previewResultButton) {
      previewResultButton.disabled = true;
      previewResultButton.dataset.downloadUrl = "";
      previewResultButton.dataset.fileName = "";
    }
    return;
  }

  const format = getResultFileFormat(file) || "файл";
  downloadResultButton.disabled = false;
  downloadResultButton.dataset.downloadUrl = file.downloadUrl;
  downloadResultButton.textContent = `Скачать ${format}`;

  if (previewResultButton) {
    const pdf = (sourceJob?.resultFiles || []).find(isPdfFile);
    previewResultButton.disabled = !pdf?.downloadUrl;
    previewResultButton.dataset.downloadUrl = pdf?.downloadUrl || "";
    previewResultButton.dataset.fileName = pdf?.fileName || "";
    previewResultButton.dataset.format = pdf?.format || "pdf";
  }
}

function getEditorSearchQuery() {
  return normalizeSearch(globalSearchInput?.value);
}

function matchesSearchValue(values, query) {
  if (!query) return true;
  return values.some(value => normalizeSearch(value).includes(query));
}

function matchesProjectOption(project, query) {
  return matchesSearchValue([
    project.name,
    project.address,
    project.factoryRequestNumber,
    project.description,
    project.ownerUserName,
    project.id
  ], query);
}

function getProjectOptionLabel(project) {
  const ownerUserName = project.ownerUserName || project.OwnerUserName || "";
  return canAdmin() && ownerUserName && ownerUserName !== state.currentUser?.userName
    ? `${project.name} · ${ownerUserName}`
    : project.name;
}

function matchesTemplateOption(template, query) {
  return matchesSearchValue([
    template.name,
    template.code,
    template.id
  ], query);
}

function matchesJobSearch(job, query) {
  return matchesSearchValue([
    job.id,
    job.templateId,
    getTemplateLabel(job.templateId),
    job.status,
    job.outputFormat,
    formatDate(job.createdAt),
    formatDate(job.finishedAt),
    ...(job.resultFiles || []).flatMap(file => [file.fileName, file.format])
  ], query);
}

function applySelectSearch(select, options, matcher, query) {
  if (!select) return;

  for (const option of select.options) {
    if (!option.value) {
      option.hidden = false;
      continue;
    }

    const item = options.find(candidate => candidate.id === option.value || candidate.code === option.value);
    const isCurrent = option.value === select.value;
    option.hidden = Boolean(query) && !isCurrent && !matcher(item || {}, query);
  }
}

function applyEditorSearch() {
  const query = getEditorSearchQuery();
  applySelectSearch(projectSelect, state.projects, matchesProjectOption, query);
  applySelectSearch(templateSelect, state.templates, matchesTemplateOption, query);
  renderJobs();
}

function getParameterType(parameter) {
  return (parameter.type || "string").toLowerCase();
}

function acceptsDecimalInput(parameter) {
  return parameter?.name === "TR";
}

function parseDecimalValue(value) {
  if (value === "" || value === null || value === undefined) return null;
  const normalized = String(value).trim().replace(",", ".");
  const number = Number(normalized);
  return Number.isFinite(number) ? number : null;
}

function hasValue(value) {
  return value !== null && value !== undefined;
}

function getDefaultValue(parameter) {
  if (hasValue(parameter.defaultValue)) return parameter.defaultValue;
  const type = getParameterType(parameter);
  return type === "bool" || type === "boolean" ? false : "";
}

function readInputValue(input, parameter) {
  const type = getParameterType(parameter);
  if (type === "bool" || type === "boolean") return input.checked;
  if (acceptsDecimalInput(parameter)) return parseDecimalValue(input.value);
  if (type === "integer") {
    if (input.value === "") return null;
    const value = Number.parseInt(input.value, 10);
    return Number.isFinite(value) ? value : null;
  }
  if (type === "number") return parseDecimalValue(input.value);
  return input.value;
}

function setInputValue(input, parameter, value) {
  if (input.type === "radio") {
    input.checked = String(value) === input.value;
    return;
  }

  const type = getParameterType(parameter);
  if (type === "bool" || type === "boolean") {
    input.checked = Boolean(value);
    return;
  }

  input.value = hasValue(value) ? value : "";
}

function rememberCurrentValues() {
  if (!state.selectedTemplate) return;

  rememberConfigurationNameValue();

  for (const input of parametersForm.querySelectorAll("input, select, textarea")) {
    if (input.type === "radio" && !input.checked) continue;

    const name = input.dataset.parameterName || input.name;
    const definition = state.selectedTemplate.parameters.find(parameter => parameter.name === name);
    if (!definition) continue;
    state.parameterValues[name] = readInputValue(input, definition);
  }
}

function getParameterValue(parameter) {
  return Object.prototype.hasOwnProperty.call(state.parameterValues, parameter.name)
    ? state.parameterValues[parameter.name]
    : getDefaultValue(parameter);
}

function getParameterDefinition(name) {
  return getTemplateDefinitions().find(parameter => parameter.name === name) || null;
}

function getParameterValueByName(name) {
  const parameter = getParameterDefinition(name);
  return parameter ? getParameterValue(parameter) : undefined;
}

function isLookupMatch(expected, actual) {
  if (typeof expected === "number") return Number(actual) === expected;
  return String(actual ?? "") === String(expected ?? "");
}

function evaluateFormulaExpression(expression, context) {
  return evaluateTFlexExpression(expression, context, {
    lookupTables: state.selectedTemplate?.lookupTables
  });
}

function getLookupValue(parameter, context) {
  if (!parameter.lookupValues?.length) return undefined;

  for (const row of parameter.lookupValues) {
    let matches = true;
    for (const [key, expected] of Object.entries(row)) {
      if (key === "value") continue;

      if (!isLookupMatch(expected, context[key])) {
        matches = false;
        break;
      }
    }

    if (matches) return row.value;
  }

  return undefined;
}

function getDisplayParameterValue(parameter, context) {
  const lookupValue = context ? getLookupValue(parameter, context) : undefined;
  if (hasValue(lookupValue)) return lookupValue;

  if (context && parameter.isReadOnly && parameter.expression) {
    const expressionValue = evaluateFormulaExpression(parameter.expression, context);
    if (hasValue(expressionValue)) return expressionValue;
  }

  return getParameterValue(parameter);
}

function getAllowedValues(parameter, context) {
  if (parameter.name.startsWith("$car_type_") && context) {
    const aa = getParameterDefinition("AA");
    const values = aa?.lookupValues
      ?.filter(row => isLookupMatch(row.cap, context.cap))
      .map(row => row.$car_type)
      .filter(hasValue) || [];

    if (values.length > 0) {
      if (parameter.allowedValues?.includes("PXX")) values.push("PXX");
      return [...new Set(values.map(value => String(value)))];
    }
  }

  return parameter.allowedValues || [];
}

function getAllowedValueLabel(parameter, value) {
  const key = String(value);
  return parameter.allowedValueLabels?.[key] || key;
}

function normalizeValueForAllowedList(parameter, value, context) {
  const allowedValues = getAllowedValues(parameter, context);
  if (allowedValues.length === 0 || !hasValue(value)) return value;
  return allowedValues.includes(String(value)) ? value : allowedValues[0];
}

function initializeParameterValues() {
  state.parameterValues = {};
  if (!state.selectedTemplate) return;

  for (const parameter of state.selectedTemplate.parameters) {
    state.parameterValues[parameter.name] = getDefaultValue(parameter);
  }
}

function getTemplateDefinitions() {
  if (!state.selectedTemplate) return [];
  return [
    ...(state.selectedTemplate.parameters || []),
    ...(state.selectedTemplate.calculatedVariables || [])
  ];
}

function toNumber(value) {
  if (value === true) return 1;
  if (value === false || value === null || value === undefined || value === "") return 0;
  const number = Number(value);
  return Number.isFinite(number) ? number : 0;
}

function toFlagNumber(value) {
  if (typeof value === "boolean") return value ? 1 : 0;
  const text = String(value ?? "").trim().toLowerCase();
  return text === "1" || text === "true" || text === "\u0434\u0430" ? 1 : 0;
}

function findNumericVariant(prefix, numericValue) {
  if (!state.selectedTemplate) return null;

  return state.selectedTemplate.parameters.find(parameter => {
    if (!parameter.name.startsWith(prefix)) return false;
    return Number(parameter.name.slice(prefix.length)) === Number(numericValue);
  });
}

function findLevelVariant(prefix, context) {
  if (!state.selectedTemplate) return null;

  return state.selectedTemplate.parameters.find(parameter =>
    parameter.name.startsWith(prefix) && isParameterVisible(parameter, context));
}

function putContextValue(context, parameter, value) {
  const type = getParameterType(parameter);
  if (type === "number" || type === "integer") {
    context[parameter.name] = toNumber(value);
  } else if (type === "bool" || type === "boolean") {
    context[parameter.name] = toFlagNumber(value);
  } else {
    context[parameter.name] = hasValue(value) ? String(value) : "";
  }
}

function applyKnownDerivedValues(context) {
  context.cwt_sg = toFlagNumber(context.$cwt_sg);
  context.dim = toFlagNumber(context.dim);
  context.load_type = context.$load_type === "\u041a\u0440\u044e\u043a\u0438" ? 1 : 2;
  context.load_mount = context.$load_type === "\u041a\u0440\u044e\u043a\u0438" && context.$load_mount === "\u0414\u0430" ? 1 : 0;
  context.$lip_type = context.$lop_type === "\u0414\u0430" ? "\u041d\u0435\u0442" : (context.$lip_type_1 || "\u041d\u0435\u0442");
  context.lip_type = context.$lip_type === "\u0414\u0430" ? 1 : 0;
  context.$A4 = context.$door_type === "\u0422\u041e" ? "\u041d\u0435\u0442" : (context.$A4_1 || "\u041d\u0435\u0442");

  if (context.$door_type === "\u0422\u041e") {
    context.A4 = Math.abs(toNumber(context.AA) / 2 - (toNumber(context.JJ) / 2 + 25));
  } else {
    context.A4 = context.$A4_1 === "\u041d\u0435\u0442" ? 0 : toNumber(context.A4_1);
  }

  context.$fire_rating = context.$PPP === "\u0414\u0430"
    ? "EI60"
    : (context.$fire_rating_1 === "\u041d\u0435\u0442" ? "\u0411\u0435\u0437 \u043e\u0433\u043d\u0435\u0441\u0442\u043e\u0439\u043a\u043e\u0441\u0442\u0438" : context.$fire_rating_1);
  context.$roller = toNumber(context.speed) === 3 ? "\u0414\u0430" : (context.$roller_1 || context.$roller || "\u041d\u0435\u0442");
  context.roller = context.$roller === "\u0414\u0430" ? 1 : 0;
}

function applyReadOnlyExpressions(context) {
  if (!state.selectedTemplate) return;

  const calculatedVariables = state.selectedTemplate.calculatedVariables || [];
  const calculatedNames = new Set(calculatedVariables.map(parameter => parameter.name));
  const definitions = [
    ...calculatedVariables,
    ...state.selectedTemplate.parameters.filter(parameter => parameter.isReadOnly)
  ];

  for (let pass = 0; pass < 8; pass += 1) {
    for (const parameter of definitions) {
      if (!parameter.expression) continue;
      const value = getLookupValue(parameter, context);
      if (hasValue(value)) {
        putContextValue(context, parameter, value);
        continue;
      }

      const expressionValue = evaluateFormulaExpression(parameter.expression, context);
      if (hasValue(expressionValue)) putContextValue(context, parameter, expressionValue);
    }

    applyKnownDerivedValues(context);
  }

  for (const parameter of state.selectedTemplate.parameters) {
    if (!parameter.isReadOnly) continue;
    if (Object.prototype.hasOwnProperty.call(context, parameter.name)) {
      state.parameterValues[parameter.name] = context[parameter.name];
    }
  }

  for (const name of calculatedNames) {
    delete state.parameterValues[name];
  }
}

function buildLevelContext() {
  const context = {
    Electric: {
      Heat: 0,
      Heat_Rel: 0,
      Regen: 0
    },
    name: 0,
    level: 0,
    main: 0,
    em: 0
  };

  if (!state.selectedTemplate) return context;

  const stopsDefinition = getParameterDefinition("stops");
  if (stopsDefinition) {
    const stops = clampStopCount(getParameterValue(stopsDefinition));
    synchronizeAutomaticStopState(stops);
    synchronizeAutomaticStopLevels(stops);
  }

  for (const parameter of getTemplateDefinitions()) {
    const value = getParameterValue(parameter);
    putContextValue(context, parameter, value);
  }

  const cap = toNumber(context.cap);
  const carTypeVariant = findLevelVariant("$car_type_", context) || findNumericVariant("$car_type_", cap);
  if (carTypeVariant) {
    const carTypeValue = normalizeValueForAllowedList(
      carTypeVariant,
      context[carTypeVariant.name] || getDefaultValue(carTypeVariant),
      context);
    context[carTypeVariant.name] = carTypeValue;
    context.$car_type = carTypeValue;
  }

  const speedVariant = findLevelVariant("$speed_", context) || findNumericVariant("$speed_", cap);
  if (speedVariant) context.speed = toNumber(context[speedVariant.name] || getDefaultValue(speedVariant));

  applyKnownDerivedValues(context);

  for (const parameter of getTemplateDefinitions()) {
    const lookupValue = getLookupValue(parameter, context);
    if (!hasValue(lookupValue)) continue;

    putContextValue(context, parameter, lookupValue);
  }

  applyKnownDerivedValues(context);
  applyReadOnlyExpressions(context);

  return context;
}

function getNormalizedTemplateMarkers(template = state.selectedTemplate) {
  if (!template) return [];
  return [
    template.id,
    template.code,
    template.name
  ].filter(Boolean).map(value => String(value)
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "_")
    .replace(/^_+|_+$/g, ""));
}

function isTemplateSupportedByPrefixes(template, prefixes) {
  const markers = getNormalizedTemplateMarkers(template);
  return markers.some(marker =>
    prefixes.some(prefix =>
      marker === prefix || marker.startsWith(`${prefix}_`)));
}

function isShaftPreviewSupportedTemplate(template = state.selectedTemplate) {
  return isTemplateSupportedByPrefixes(template, SHAFT_PREVIEW_SUPPORTED_TEMPLATE_PREFIXES);
}

function isEscalatorPreviewSupportedTemplate(template = state.selectedTemplate) {
  return isTemplateSupportedByPrefixes(template, ESCALATOR_PREVIEW_SUPPORTED_TEMPLATE_PREFIXES);
}

function getDynamicPreviewType(template = state.selectedTemplate) {
  if (isShaftPreviewSupportedTemplate(template)) return "shaft";
  if (isEscalatorPreviewSupportedTemplate(template)) return "escalator";
  return null;
}

function getPreviewNumber(context, name) {
  const value = context?.[name] ?? getParameterValueByName(name);
  const number = toNumber(value);
  return Number.isFinite(number) && number > 0 ? number : null;
}

function getPreviewOptionalNumber(context, name) {
  const value = context?.[name] ?? getParameterValueByName(name);
  if (!hasValue(value) || String(value).trim() === "") return null;
  const number = Number(String(value).replace(",", "."));
  return Number.isFinite(number) ? number : null;
}

function getPreviewSignedNumber(context, name) {
  const value = context?.[name] ?? getParameterValueByName(name);
  const number = Number(value);
  return Number.isFinite(number) ? number : 0;
}

function getPreviewFlag(context, name) {
  const value = context?.[name] ?? getParameterValueByName(name);
  return toFlagNumber(value);
}

function getPreviewRawValue(context, ...names) {
  for (const name of names) {
    const value = context?.[name] ?? getParameterValueByName(name);
    if (hasValue(value) && String(value).trim() !== "") return value;
  }

  return undefined;
}

function getPreviewTextValue(context, ...names) {
  const value = getPreviewRawValue(context, ...names);
  return hasValue(value) ? String(value).trim() : "";
}

function normalizePreviewToken(value) {
  return String(value || "")
    .trim()
    .toLowerCase()
    .replaceAll(" ", "")
    .replaceAll("-", "");
}

function isCenterOpeningDoor(context) {
  const opening = normalizePreviewToken(getPreviewTextValue(
    context,
    "$Opening",
    "Opening",
    "$door_type",
    "door_type",
    "$DOOR",
    "DOOR"));

  return opening === "co"
    || opening === "2co"
    || opening === "цо"
    || opening === "cld";
}

function getCounterweightPlace(context) {
  const place = normalizePreviewToken(getPreviewTextValue(context, "$s", "s", "$HAND", "HAND"));
  if (place === "справа" || place === "направо" || place === "right" || place === "1") {
    return { label: place === "направо" ? "Направо" : "Справа", mirrorX: true };
  }

  return { label: place === "налево" ? "Налево" : "Слева", mirrorX: false };
}

function getRearDoorDirection(context) {
  const direction = normalizePreviewToken(getPreviewTextValue(context, "$s_1", "s_1"));
  const opensRight = direction === "направо" || direction === "right" || direction === "1";
  return {
    value: opensRight ? "right" : "left",
    label: opensRight ? "Направо" : "Налево"
  };
}

function getPreviewLayoutType(template = state.selectedTemplate) {
  const marker = [template?.id, template?.code, template?.name]
    .filter(Boolean)
    .map(value => String(value).toLowerCase())
    .join(" ");

  if (marker.includes("rear") || marker.includes("back") || marker.includes("зад")) {
    return "rear";
  }

  return "side";
}

function isLehyProTemplate(template = state.selectedTemplate) {
  return template?.id === "lehy_pro_side_cwt"
    || template?.id === "lehy_pro_rear_cwt";
}

function getPreviewVariantNumber(context, prefix, fallbackNames = []) {
  const visibleVariant = findLevelVariant(prefix, context);
  if (visibleVariant) {
    const value = getPreviewNumber(context, visibleVariant.name);
    if (value) return value;
  }

  for (const name of fallbackNames) {
    const value = getPreviewNumber(context, name);
    if (value) return value;
  }

  return null;
}

function getPreviewKk(context, centerOpeningDoor) {
  const kk = getPreviewNumber(context, "KK");
  if (kk) return kk;

  if (state.selectedTemplate?.id === "lehy_pro_side_cwt") {
    return centerOpeningDoor ? 80 : 45;
  }

  return centerOpeningDoor ? 55 : 45;
}

function clampPreviewNumber(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function formatPreviewNumber(value) {
  if (!Number.isFinite(value)) return "-";
  return String(Math.round(value));
}

function formatPreviewMetricValue(value) {
  if (typeof value === "number") return formatPreviewNumber(value);
  return escapeHtml(value);
}

function showShaftPreviewUnavailable(message, title = "Предпросмотр") {
  if (previewPanelTitle) previewPanelTitle.textContent = title;
  if (shaftPreviewSubtitle) shaftPreviewSubtitle.textContent = "Preview недоступен";
  if (shaftPreviewUnavailable) {
    shaftPreviewUnavailable.hidden = false;
    shaftPreviewUnavailable.textContent = message;
  }
  if (shaftPreviewContent) shaftPreviewContent.hidden = true;
}

function showPreviewContent({ title, subtitle, ariaLabel, svg, metrics, technical = false }) {
  if (previewPanelTitle) previewPanelTitle.textContent = title;
  if (shaftPreviewSubtitle) shaftPreviewSubtitle.textContent = subtitle;
  if (shaftPreviewUnavailable) shaftPreviewUnavailable.hidden = true;
  if (shaftPreviewCanvas) {
    shaftPreviewCanvas.classList.toggle("is-technical-preview", technical);
    shaftPreviewCanvas.setAttribute("aria-label", ariaLabel);
    shaftPreviewCanvas.innerHTML = svg;
  }
  if (shaftPreviewMetrics) shaftPreviewMetrics.innerHTML = metrics;
  if (shaftPreviewContent) shaftPreviewContent.hidden = false;
}

function updateShaftPreview(context = null) {
  if (!shaftPreviewContent || !shaftPreviewCanvas || !shaftPreviewMetrics) return;

  if (!state.selectedTemplate) {
    showShaftPreviewUnavailable("Выберите шаблон, чтобы увидеть предпросмотр.", "Предпросмотр");
    return;
  }

  const previewType = getDynamicPreviewType();
  if (!previewType) {
    showShaftPreviewUnavailable(
      "Предпросмотр доступен для шаблонов LEHY-L-PRO, LEHY-PRO и K-II-TYPE.",
      "Предпросмотр");
    return;
  }

  const previewContext = context || buildLevelContext();

  if (previewType === "escalator") {
    updateEscalatorPreview(previewContext);
    return;
  }

  const ah = getPreviewNumber(previewContext, "AH");
  const bh = getPreviewNumber(previewContext, "BH");
  const aa = getPreviewNumber(previewContext, "AA");
  const bb = getPreviewNumber(previewContext, "BB");
  const jj = getPreviewNumber(previewContext, "JJ");
  const ee = getPreviewNumber(previewContext, "EE");
  const ww = getPreviewNumber(previewContext, "WW") || getPreviewVariantNumber(previewContext, "WW_", [
    "WW_1",
    "WW_11",
    "WW_12",
    "WW_2",
    "WW_21",
    "WW_22",
    "WW_3",
    "WW_31",
    "WW_32"
  ]);
  const wg = getPreviewNumber(previewContext, "WG") || getPreviewVariantNumber(previewContext, "WG_", [
    "WG_1",
    "WG_11",
    "WG_12",
    "WG_13",
    "WG_2",
    "WG_21",
    "WG_22",
    "WG_23",
    "WG_3"
  ]);
  const a1 = getPreviewNumber(previewContext, "A1");
  const a2 = getPreviewNumber(previewContext, "A2");
  const bw = getPreviewNumber(previewContext, "BW");
  const ca = getPreviewNumber(previewContext, "CA");
  const cb = getPreviewNumber(previewContext, "CB");
  const cc = getPreviewNumber(previewContext, "CC");
  const as = getPreviewNumber(previewContext, "AS") || (aa ? aa + 62 : null);
  const a6 = getPreviewNumber(previewContext, "A6");
  const b1 = getPreviewNumber(previewContext, "B1");
  const bs = getPreviewNumber(previewContext, "BS");
  const dk = getPreviewNumber(previewContext, "DK");
  const bottomGap = getPreviewNumber(previewContext, "bb");
  const cwtPlace = getCounterweightPlace(previewContext);
  const rearDoorDirection = getRearDoorDirection(previewContext);
  const cwtLayout = getPreviewLayoutType();
  const centerOpeningDoor = isCenterOpeningDoor(previewContext);
  const entrances = Math.max(1, Math.round(getPreviewNumber(previewContext, "NE") || 1));
  const kk = getPreviewKk(previewContext, centerOpeningDoor);
  const doorWidth = jj ? (centerOpeningDoor ? jj * 2 : jj * 1.5 + 175) : null;
  const doorWidthMetricName = centerOpeningDoor ? "2xJJ" : "1.5*JJ+175";
  const lehyProSideCwt = state.selectedTemplate?.id === "lehy_pro_side_cwt";
  const lehyPro = isLehyProTemplate();
  const rearCwt = lehyPro && cwtLayout === "rear";
  const carCenterX = rearCwt && ah
    ? (cb ? ah - cb : ah / 2)
    : (a1 && a2
    ? a1 + a2
    : (lehyProSideCwt && cb && ah ? ah - cb : (ca || (ah ? ah / 2 : null))));
  const sideCwtCenterX = a1
    ?? (lehyProSideCwt && ca && carCenterX ? carCenterX - ca : null)
    ?? (bw && ah ? ah - bw : null);
  const sideCwtX = sideCwtCenterX && ww ? sideCwtCenterX - ww / 2 : null;
  const rearCwtX = carCenterX && wg ? carCenterX - wg / 2 : null;
  const cwtX = rearCwt ? rearCwtX : sideCwtX;
  const cwtMetricName = rearCwt
    ? "WG"
    : (a1 ? "A1" : (lehyProSideCwt && ca ? "CA" : (bw ? "AH-BW" : "CWT X")));
  const cwtMetricValue = rearCwt
    ? wg
    : (a1 ?? (lehyProSideCwt && ca ? ca : sideCwtCenterX));
  const cwtDepth = rearCwt
    ? ww
    : (ee
      ? ee + 150
      : (bh && wg ? Math.max(0, (bh - wg) / 2) : null));
  const rearCabinWall = entrances > 1 ? (dk || 141) : (bottomGap || 30);
  const rearCabinOuterY = rearCwt && cc && bb
    ? cc - bb / 2 - rearCabinWall
    : null;
  const cwtRearClearance = rearCwt && rearCabinOuterY !== null && ww
    ? Math.max(0, rearCabinOuterY - ww - 30)
    : null;
  const cwtY = rearCwt
    ? cwtRearClearance
    : (ee && bh && wg ? bh - cwtDepth - wg : cwtDepth);
  const doorOffset = rearCwt
    ? getPreviewSignedNumber(previewContext, "CJ")
    : getPreviewSignedNumber(previewContext, "A4");
  const dimensions = {
    ah,
    bh,
    aa,
    bb,
    as,
    jj,
    kk,
    doorWidth,
    doorWidthMetricName,
    centerOpeningDoor,
    entrances,
    cwtPlaceLabel: cwtPlace.label,
    cwtLayout,
    mirrorX: rearCwt ? false : cwtPlace.mirrorX,
    rearDoorDirection: rearDoorDirection.value,
    rearDoorDirectionLabel: rearDoorDirection.label,
    a4: doorOffset,
    a1,
    a2,
    a6,
    bw,
    ca,
    cb,
    cc,
    carCenterX,
    carCenterY: rearCwt ? cc : null,
    ee,
    cwtX,
    cwtMetricName,
    cwtMetricValue,
    cwtDepth,
    cwtY,
    cwtRearClearance,
    cwtDepthLabel: rearCwt ? "WW" : (ee ? "EE+150" : "CWT Y"),
    lehyPro,
    rearCwt,
    bs,
    dk,
    bottomGap,
    ww,
    wg
  };

  if (!dimensions.ah || !dimensions.bh || !dimensions.aa || !dimensions.bb) {
    showShaftPreviewUnavailable("Недостаточно размеров AH, BH, AA и BB для построения плана.", "План шахты");
    return;
  }

  showPreviewContent({
    title: "План шахты",
    subtitle: "Live preview по текущим параметрам",
    ariaLabel: "Динамический план шахты",
    svg: renderShaftPreviewSvg(dimensions),
    metrics: renderShaftPreviewMetrics(dimensions)
  });
}

function updateEscalatorPreview(previewContext) {
  const rise = getPreviewNumber(previewContext, "HE");
  const angle = getPreviewNumber(previewContext, "alpha");
  const lowerLanding = getPreviewNumber(previewContext, "TK") || getPreviewNumber(previewContext, "TK_v");
  const upperLanding = getPreviewNumber(previewContext, "TJ") || getPreviewNumber(previewContext, "TJ_v");
  const stepWidth = getPreviewNumber(previewContext, "W");
  const speed = getPreviewOptionalNumber(previewContext, "V") ?? getPreviewOptionalNumber(previewContext, "V_v");
  const escalatorCount = Math.max(1, Math.round(getPreviewNumber(previewContext, "N") || 1));
  const angleRadians = angle ? angle * Math.PI / 180 : 0;
  const calculatedInclineRun = rise && Math.tan(angleRadians)
    ? rise / Math.tan(angleRadians)
    : null;
  const templateTotalRun = getPreviewNumber(previewContext, "TG");
  const inclineRun = Math.max(
    calculatedInclineRun || 0,
    templateTotalRun && lowerLanding && upperLanding ? templateTotalRun - lowerLanding - upperLanding : 0);
  const totalRun = lowerLanding && upperLanding && inclineRun
    ? lowerLanding + inclineRun + upperLanding
    : templateTotalRun;
  const overallLength = getPreviewNumber(previewContext, "LL") || (totalRun ? totalRun + 240 : null);
  const pitLength = getPreviewNumber(previewContext, "Lpit") || getPreviewNumber(previewContext, "Lpit_v");
  const pitDepth = getPreviewNumber(previewContext, "Dpit") || getPreviewNumber(previewContext, "Dpit_v");
  const trussDepth = getPreviewNumber(previewContext, "D") || (angle === 35 ? 1800 : 2000);
  const balustradeHeight = getPreviewNumber(previewContext, "CH") || 1000;
  const pitWidth = getPreviewNumber(previewContext, "Wpit");
  const mountingGap = getPreviewNumber(previewContext, "HGAP");
  const support1 = getPreviewNumber(previewContext, "SUP1");
  const support2 = getPreviewNumber(previewContext, "SUP2");
  const support1Mode = getPreviewOptionalNumber(previewContext, "s1");
  const support2Mode = getPreviewOptionalNumber(previewContext, "s2");
  const mode = getPreviewTextValue(previewContext, "$Mode") || "Нормальный";
  const pitType = getPreviewTextValue(previewContext, "$pit_v") || "Отверстие в плите";
  const balustrade = getPreviewTextValue(previewContext, "$Balustrade_material") || "Стекло";

  if (!rise || !angle || !lowerLanding || !upperLanding || !totalRun || !inclineRun) {
    showShaftPreviewUnavailable(
      "Недостаточно размеров HE, alpha, TK и TJ для построения профиля эскалатора.",
      "Профиль эскалатора");
    return;
  }

  const dimensions = {
    rise,
    angle,
    lowerLanding,
    upperLanding,
    inclineRun,
    totalRun,
    overallLength,
    stepWidth,
    speed,
    escalatorCount,
    pitLength,
    pitDepth,
    trussDepth,
    balustradeHeight,
    pitWidth,
    mountingGap,
    support1,
    support2,
    support1Visible: support1Mode === 0,
    support2Visible: support2Mode === 0,
    mode,
    pitType,
    balustrade
  };

  showPreviewContent({
    title: "Профиль эскалатора",
    subtitle: "Live preview по текущим параметрам",
    ariaLabel: "Динамический профиль эскалатора",
    svg: renderEscalatorPreviewSvg(dimensions),
    metrics: renderEscalatorPreviewMetrics(dimensions)
  });
}

function getEscalatorProfileY(dimensions, x) {
  const inclineStart = dimensions.lowerLanding;
  const inclineEnd = dimensions.lowerLanding + dimensions.inclineRun;
  if (x <= inclineStart) return 0;
  if (x >= inclineEnd) return dimensions.rise;
  return ((x - inclineStart) / dimensions.inclineRun) * dimensions.rise;
}

function renderEscalatorPreviewSvg(dimensions) {
  const svgWidth = 420;
  const svgHeight = 300;
  const paddingX = 18;
  const paddingY = 30;
  const totalRun = dimensions.totalRun || KII_ESCALATOR_REFERENCE.totalRun;
  const rise = dimensions.rise || KII_ESCALATOR_REFERENCE.rise;
  const lowerLanding = dimensions.lowerLanding || KII_ESCALATOR_REFERENCE.lowerLanding;
  const upperLanding = dimensions.upperLanding || KII_ESCALATOR_REFERENCE.upperLanding;
  const inclineRun = dimensions.inclineRun || Math.max(totalRun - lowerLanding - upperLanding, rise);
  const inclineEnd = lowerLanding + inclineRun;
  const pitLength = dimensions.pitLength || KII_ESCALATOR_REFERENCE.pitLength;
  const pitDepth = dimensions.pitDepth || KII_ESCALATOR_REFERENCE.pitDepth;
  const pitToken = normalizePreviewToken(dimensions.pitType);
  const hasPit = pitToken.includes("приям") || pitToken.includes("pit");
  const floorThickness = clampPreviewNumber(rise * 0.045, 150, 260);
  const pitWallThickness = clampPreviewNumber(floorThickness * 0.82, 130, 220);
  const slabOverhang = clampPreviewNumber(totalRun * 0.06, 600, 1100);
  const lowerRightSlabWidth = slabOverhang * 1.35;
  const lowerOpeningEnd = Math.max(pitLength, lowerLanding + floorThickness * 2);
  const pdfToMmX = totalRun / KII_ESCALATOR_REFERENCE.pdfRun;
  const pdfToMmY = rise / KII_ESCALATOR_REFERENCE.pdfRise;
  const upperTrussRightX = 831.08 * pdfToMmX;
  const upperOpeningStart = clampPreviewNumber(
    lowerLanding + inclineRun * 0.45,
    lowerLanding + floorThickness * 2,
    Math.max(lowerLanding + floorThickness * 2, inclineEnd - upperLanding * 0.25)
  );
  const upperOpeningEnd = upperTrussRightX + floorThickness * 0.08;
  const pdfBoundsMm = {
    minX: KII_ESCALATOR_PDF_BOUNDS.minX * pdfToMmX,
    minY: KII_ESCALATOR_PDF_BOUNDS.minY * pdfToMmY,
    maxX: KII_ESCALATOR_PDF_BOUNDS.maxX * pdfToMmX,
    maxY: KII_ESCALATOR_PDF_BOUNDS.maxY * pdfToMmY
  };
  const constructionBounds = {
    minX: -slabOverhang,
    minY: -Math.max(pitDepth + pitWallThickness, floorThickness),
    maxX: Math.max(totalRun + slabOverhang, upperOpeningEnd + slabOverhang),
    maxY: Math.max(rise + floorThickness, pdfBoundsMm.maxY)
  };
  const bounds = {
    minX: Math.min(pdfBoundsMm.minX, constructionBounds.minX),
    minY: Math.min(pdfBoundsMm.minY, constructionBounds.minY),
    maxX: Math.max(pdfBoundsMm.maxX, constructionBounds.maxX),
    maxY: Math.max(pdfBoundsMm.maxY, constructionBounds.maxY)
  };
  const boundsWidth = bounds.maxX - bounds.minX;
  const boundsHeight = bounds.maxY - bounds.minY;
  const scale = Math.min(
    (svgWidth - paddingX * 2) / boundsWidth,
    (svgHeight - paddingY * 2) / boundsHeight
  );
  const translateX = paddingX - bounds.minX * scale;
  const translateY = paddingY + bounds.maxY * scale;
  const slabRects = [
    ...(!hasPit ? [
      { x: -slabOverhang, y: -floorThickness, width: slabOverhang, height: floorThickness },
      { x: lowerOpeningEnd, y: -floorThickness, width: lowerRightSlabWidth, height: floorThickness }
    ] : []),
    { x: -slabOverhang, y: rise - floorThickness, width: upperOpeningStart + slabOverhang, height: floorThickness },
    { x: upperOpeningEnd, y: rise - floorThickness, width: slabOverhang, height: floorThickness }
  ].filter(rect => rect.width > 8 && rect.height > 8);
  const slabMarkup = slabRects
    .map(rect => `
      <rect class="escalator-preview-svg__slab" x="${rect.x.toFixed(1)}" y="${rect.y.toFixed(1)}" width="${rect.width.toFixed(1)}" height="${rect.height.toFixed(1)}" />`)
    .join("");
  const pitWallMarkup = hasPit
    ? `<path class="escalator-preview-svg__pit-wall" d="
        M ${(-slabOverhang).toFixed(1)} 0
        L 0 0
        L 0 ${(-pitDepth).toFixed(1)}
        L ${lowerOpeningEnd.toFixed(1)} ${(-pitDepth).toFixed(1)}
        L ${lowerOpeningEnd.toFixed(1)} 0
        L ${(lowerOpeningEnd + lowerRightSlabWidth).toFixed(1)} 0
        L ${(lowerOpeningEnd + lowerRightSlabWidth).toFixed(1)} ${(-floorThickness).toFixed(1)}
        L ${(lowerOpeningEnd + pitWallThickness).toFixed(1)} ${(-floorThickness).toFixed(1)}
        L ${(lowerOpeningEnd + pitWallThickness).toFixed(1)} ${(-pitDepth - pitWallThickness).toFixed(1)}
        L ${(-pitWallThickness).toFixed(1)} ${(-pitDepth - pitWallThickness).toFixed(1)}
        L ${(-pitWallThickness).toFixed(1)} ${(-floorThickness).toFixed(1)}
        L ${(-slabOverhang).toFixed(1)} ${(-floorThickness).toFixed(1)}
        Z" />`
    : "";
  const lowerOpeningMarkup = hasPit
    ? ""
    : `<path class="escalator-preview-svg__opening-edge" d="M 0 0 L 0 ${(-pitDepth).toFixed(1)} L ${lowerOpeningEnd.toFixed(1)} ${(-pitDepth).toFixed(1)} L ${lowerOpeningEnd.toFixed(1)} 0" />`;
  const upperOpeningMarkup = `
    <path class="escalator-preview-svg__opening-edge" d="
      M ${upperOpeningStart.toFixed(1)} ${rise.toFixed(1)} L ${upperOpeningStart.toFixed(1)} ${(rise - floorThickness).toFixed(1)}
      M ${upperOpeningEnd.toFixed(1)} ${rise.toFixed(1)} L ${upperOpeningEnd.toFixed(1)} ${(rise - floorThickness).toFixed(1)}" />`;
  const paths = KII_ESCALATOR_PDF_PATHS
    .filter(path => !KII_ESCALATOR_EXCLUDED_PDF_PATHS.has(path))
    .map((path) => `<path class="escalator-preview-svg__pdf-line" d="${path}" />`)
    .join("");

  return `
    <svg class="shaft-preview-svg escalator-preview-svg" viewBox="0 0 ${svgWidth} ${svgHeight}" role="img" aria-label="Профиль эскалатора">
      <defs>
        <pattern id="escalatorConcreteHatch" width="9" height="9" patternUnits="userSpaceOnUse" patternTransform="rotate(28)">
          <rect class="escalator-preview-svg__concrete-fill" x="0" y="0" width="9" height="9" />
          <line class="escalator-preview-svg__slab-hatch" x1="0" y1="0" x2="0" y2="9" />
        </pattern>
      </defs>
      <g transform="translate(${translateX.toFixed(2)} ${translateY.toFixed(2)}) scale(${scale.toFixed(5)} ${(-scale).toFixed(5)})">
        ${slabMarkup}
        ${pitWallMarkup}
        ${lowerOpeningMarkup}
        ${upperOpeningMarkup}
        <g transform="scale(${pdfToMmX.toFixed(5)} ${pdfToMmY.toFixed(5)})">
          ${paths}
        </g>
      </g>
    </svg>`;
}

function renderEscalatorHatch(rect, className = "escalator-preview-svg__slab-hatch") {
  const spacing = 180;
  const lines = [];
  const start = rect.x - rect.height;
  const end = rect.x + rect.width;

  for (let x = start; x < end; x += spacing) {
    const x1 = clampPreviewNumber(x, rect.x, rect.x + rect.width);
    const x2 = clampPreviewNumber(x + rect.height, rect.x, rect.x + rect.width);
    const y1 = rect.y + (x1 - x);
    const y2 = rect.y + rect.height - (x + rect.height - x2);
    lines.push(`<line class="${className}" x1="${x1.toFixed(1)}" y1="${y1.toFixed(1)}" x2="${x2.toFixed(1)}" y2="${y2.toFixed(1)}" />`);
  }

  return lines.join("");
}

function renderEscalatorPreviewMetrics(dimensions) {
  const supportCount = [
    dimensions.support1Visible,
    dimensions.support2Visible
  ].filter(Boolean).length;
  const metrics = [
    ["HE", dimensions.rise, "Высота подъема"],
    ["alpha", `${formatPreviewNumber(dimensions.angle)}°`, "Угол наклона"],
    ["TG", dimensions.totalRun, "Горизонтальная длина"],
    ["LL", dimensions.overallLength, "Габаритная длина"],
    ["W", dimensions.stepWidth, "Ширина ступеней"],
    ["N", dimensions.escalatorCount, "Количество эскалаторов"],
    ["Wpit", dimensions.pitWidth, "Ширина проема"],
    ["TK", dimensions.lowerLanding, "Нижняя площадка"],
    ["TJ", dimensions.upperLanding, "Верхняя площадка"],
    ["Lpit", dimensions.pitLength, "Длина приямка"],
    ["Dpit", dimensions.pitDepth, "Глубина приямка"],
    ["SUP", supportCount > 0 ? supportCount : "Нет", "Промежуточные опоры"],
    ["V", hasValue(dimensions.speed) ? `${dimensions.speed} м/с` : null, "Скорость"],
    ["Режим", dimensions.mode, "Режим работы"],
    ["Балюстрада", dimensions.balustrade, "Материал"],
    ["Исполнение", dimensions.pitType, "Приямок"]
  ];

  return metrics
    .filter(([, value]) => value !== null && value !== undefined && value !== "")
    .map(([name, value, label]) => `
      <div class="shaft-preview__metric">
        <dt>${escapeHtml(name)}</dt>
        <dd>${formatPreviewMetricValue(value)}<span>${escapeHtml(label)}</span></dd>
      </div>`)
    .join("");
}

function renderShaftPreviewSvg(dimensions) {
  const svgWidth = 380;
  const svgHeight = 286;
  const paddingX = 34;
  const paddingY = 30;
  const drawingWidth = svgWidth - paddingX * 2;
  const drawingHeight = svgHeight - paddingY * 2;

  const shaftRect = { x: 0, y: 0, width: dimensions.ah, height: dimensions.bh };
  const shaftWallThickness = clampPreviewNumber(Math.min(dimensions.ah, dimensions.bh) * 0.075, 95, 160);
  const shaftConcreteOuterRect = {
    x: -shaftWallThickness,
    y: -shaftWallThickness,
    width: dimensions.ah + shaftWallThickness * 2,
    height: dimensions.bh + shaftWallThickness * 2
  };
  const cabinSideWallMm = 31;
  const cabinRearWallMm = dimensions.rearCwt
    ? (dimensions.entrances > 1 ? (dimensions.dk || 141) : (dimensions.bottomGap || 30))
    : 30;
  const cabinFrontWallMm = dimensions.rearCwt
    ? (dimensions.dk || 141)
    : (dimensions.kk || 45);
  const cabinInnerX = dimensions.carCenterX
    ? dimensions.carCenterX - dimensions.aa / 2
    : (dimensions.ah - dimensions.aa) / 2;
  const cabinOuterWidth = Math.max(dimensions.as || 0, dimensions.aa + cabinSideWallMm * 2);
  const cabinOuterHeight = dimensions.rearCwt && dimensions.bs
    ? dimensions.bs
    : dimensions.bb + cabinRearWallMm + cabinFrontWallMm;
  const cabinSideWall = (cabinOuterWidth - dimensions.aa) / 2;
  const doorWidthMm = dimensions.doorWidth || dimensions.aa * 0.55;
  const doorDepthMm = dimensions.centerOpeningDoor ? 60 : 96;
  const doorSpacingMm = 30;
  const carDoorFrontGap = dimensions.centerOpeningDoor ? 130 : 151;
  const cabinOuterTopY = dimensions.rearCwt && dimensions.carCenterY
    ? dimensions.carCenterY - dimensions.bb / 2 - cabinRearWallMm
    : dimensions.bh - carDoorFrontGap - doorDepthMm - cabinOuterHeight;
  const baseCabinOuterRect = {
    x: cabinInnerX - cabinSideWall,
    y: cabinOuterTopY,
    width: cabinOuterWidth,
    height: cabinOuterHeight
  };
  const baseCabinInnerRect = {
    x: baseCabinOuterRect.x + cabinSideWall,
    y: baseCabinOuterRect.y + cabinRearWallMm,
    width: dimensions.aa,
    height: dimensions.bb
  };
  const doorX = dimensions.centerOpeningDoor
    ? baseCabinInnerRect.x + baseCabinInnerRect.width / 2 + (dimensions.a4 || 0) - doorWidthMm / 2
    : dimensions.rearCwt && dimensions.rearDoorDirection === "right"
      ? baseCabinOuterRect.x - 25
      : baseCabinOuterRect.x + baseCabinOuterRect.width + 25 - doorWidthMm;
  const makeDoorPair = side => {
    const isRear = side === "rear";
    const carDoorY = dimensions.lehyPro
      ? (isRear
        ? baseCabinOuterRect.y
        : baseCabinOuterRect.y + baseCabinOuterRect.height - doorDepthMm)
      : (isRear
        ? baseCabinOuterRect.y - doorDepthMm
        : baseCabinOuterRect.y + baseCabinOuterRect.height);
    const landingDoorY = isRear
      ? carDoorY - doorSpacingMm - doorDepthMm
      : carDoorY + doorDepthMm + doorSpacingMm;

    return {
      side,
      carDoor: {
        x: doorX,
        y: carDoorY,
        width: doorWidthMm,
        height: doorDepthMm
      },
      landingDoor: {
        x: doorX,
        y: landingDoorY,
        width: doorWidthMm,
        height: doorDepthMm
      }
    };
  };
  const baseDoorPairs = [makeDoorPair("front")];
  if (dimensions.entrances > 1) {
    baseDoorPairs.push(makeDoorPair("rear"));
  }

  const buildBaseCwtRect = () => {
    if (!dimensions.ww || !dimensions.wg) return null;

    const gap = 30;
    if (dimensions.rearCwt) {
      const width = dimensions.wg;
      const height = dimensions.ww;
      const centerX = dimensions.carCenterX || (baseCabinInnerRect.x + baseCabinInnerRect.width / 2);
      return {
        x: centerX - width / 2,
        y: Number.isFinite(dimensions.cwtY) ? dimensions.cwtY : gap,
        width,
        height
      };
    }

    const width = dimensions.ww;
    const height = dimensions.wg;
    const measuredX = Number.isFinite(dimensions.cwtX) && dimensions.cwtX + width <= baseCabinOuterRect.x + 20
      ? dimensions.cwtX
      : baseCabinOuterRect.x - width - 48;
    const measuredY = Number.isFinite(dimensions.cwtY)
      ? dimensions.cwtY
      : (dimensions.bh - height) / 2;

    return {
      x: clampPreviewNumber(measuredX, gap, Math.max(gap, baseCabinOuterRect.x - width - 22)),
      y: clampPreviewNumber(measuredY, gap, Math.max(gap, dimensions.bh - height - gap)),
      width,
      height
    };
  };

  const baseCwtRect = buildBaseCwtRect();
  const mirrorRect = rect => dimensions.mirrorX && !dimensions.rearCwt
    ? { ...rect, x: dimensions.ah - rect.x - rect.width }
    : rect;
  const mirrorDoorPair = pair => ({
    ...pair,
    carDoor: mirrorRect(pair.carDoor),
    landingDoor: mirrorRect(pair.landingDoor)
  });
  const cabinOuterRect = mirrorRect(baseCabinOuterRect);
  const cabinInnerRect = mirrorRect(baseCabinInnerRect);
  const doorPairs = baseDoorPairs.map(mirrorDoorPair);
  const cwtRect = baseCwtRect ? mirrorRect(baseCwtRect) : null;
  const doorRects = doorPairs.flatMap(pair => [pair.carDoor, pair.landingDoor]);
  const rects = [shaftConcreteOuterRect, shaftRect, cabinOuterRect, cabinInnerRect, ...doorRects, ...(cwtRect ? [cwtRect] : [])];
  const margin = 140;
  const bounds = rects.reduce((acc, rect) => ({
    minX: Math.min(acc.minX, rect.x),
    minY: Math.min(acc.minY, rect.y),
    maxX: Math.max(acc.maxX, rect.x + rect.width),
    maxY: Math.max(acc.maxY, rect.y + rect.height)
  }), { minX: 0, minY: 0, maxX: dimensions.ah, maxY: dimensions.bh });
  bounds.minX -= margin;
  bounds.minY -= margin;
  bounds.maxX += margin;
  bounds.maxY += margin;

  const scale = Math.min(drawingWidth / (bounds.maxX - bounds.minX), drawingHeight / (bounds.maxY - bounds.minY));
  const mapX = value => paddingX + (value - bounds.minX) * scale;
  const mapY = value => paddingY + (value - bounds.minY) * scale;
  const mapSize = value => value * scale;
  const rectAttrs = rect =>
    `x="${mapX(rect.x).toFixed(1)}" y="${mapY(rect.y).toFixed(1)}" width="${mapSize(rect.width).toFixed(1)}" height="${mapSize(rect.height).toFixed(1)}"`;
  const lineAttrs = (x1, y1, x2, y2) =>
    `x1="${mapX(x1).toFixed(1)}" y1="${mapY(y1).toFixed(1)}" x2="${mapX(x2).toFixed(1)}" y2="${mapY(y2).toFixed(1)}"`;
  const pathPoint = (x, y) => `${mapX(x).toFixed(1)} ${mapY(y).toFixed(1)}`;
  const rectPath = rect => [
    `M ${pathPoint(rect.x, rect.y)}`,
    `L ${pathPoint(rect.x + rect.width, rect.y)}`,
    `L ${pathPoint(rect.x + rect.width, rect.y + rect.height)}`,
    `L ${pathPoint(rect.x, rect.y + rect.height)}`,
    "Z"
  ].join(" ");
  const pillRadius = Math.max(2, Math.min(9, mapSize(doorDepthMm / 2))).toFixed(1);
  const isInside = (inner, outer) =>
    inner.x >= outer.x
    && inner.y >= outer.y
    && inner.x + inner.width <= outer.x + outer.width
    && inner.y + inner.height <= outer.y + outer.height;
  const intersects = (first, second) =>
    first.x < second.x + second.width
    && first.x + first.width > second.x
    && first.y < second.y + second.height
    && first.y + first.height > second.y;
  const cabinCollision = !isInside(cabinOuterRect, shaftRect) || (cwtRect && intersects(cabinOuterRect, cwtRect));
  const cwtCollision = cwtRect && (!isInside(cwtRect, shaftRect) || intersects(cabinOuterRect, cwtRect));
  const getCabinDoorOpeningBounds = pair => {
    const openingWidth = dimensions.jj || pair.carDoor.width;
    let start;
    let end;

    if (dimensions.centerOpeningDoor) {
      const center = pair.carDoor.x + pair.carDoor.width / 2;
      start = center - openingWidth / 2;
      end = center + openingWidth / 2;
    } else if (dimensions.rearCwt && dimensions.rearDoorDirection === "right") {
      start = cabinInnerRect.x + 25;
      end = start + openingWidth;
    } else if (dimensions.mirrorX) {
      start = cabinInnerRect.x + 25;
      end = start + openingWidth;
    } else {
      end = cabinInnerRect.x + cabinInnerRect.width - 25;
      start = end - openingWidth;
    }

    const outerLeft = cabinOuterRect.x;
    const outerRight = cabinOuterRect.x + cabinOuterRect.width;
    return {
      start: clampPreviewNumber(start, outerLeft, outerRight),
      end: clampPreviewNumber(end, outerLeft, outerRight)
    };
  };
  const shaftLeft = shaftRect.x;
  const shaftRight = shaftRect.x + shaftRect.width;
  const shaftTop = shaftRect.y;
  const shaftBottom = shaftRect.y + shaftRect.height;
  const getShaftDoorOpeningBounds = pair => {
    const cabinOpening = getCabinDoorOpeningBounds(pair);
    const start = clampPreviewNumber(cabinOpening.start - 100, shaftLeft, shaftRight);
    const end = clampPreviewNumber(cabinOpening.end + 100, shaftLeft, shaftRight);
    return {
      start: Math.min(start, end),
      end: Math.max(start, end)
    };
  };
  const frontOpening = getShaftDoorOpeningBounds(doorPairs[0]);
  const rearOpening = doorPairs[1] ? getShaftDoorOpeningBounds(doorPairs[1]) : null;
  const concreteCollisionRects = [
    {
      x: shaftLeft - shaftWallThickness,
      y: shaftTop - shaftWallThickness,
      width: shaftWallThickness,
      height: dimensions.bh + shaftWallThickness * 2
    },
    {
      x: shaftRight,
      y: shaftTop - shaftWallThickness,
      width: shaftWallThickness,
      height: dimensions.bh + shaftWallThickness * 2
    },
    {
      x: shaftLeft - shaftWallThickness,
      y: shaftBottom,
      width: frontOpening.start - shaftLeft + shaftWallThickness,
      height: shaftWallThickness
    },
    {
      x: frontOpening.end,
      y: shaftBottom,
      width: shaftRight - frontOpening.end + shaftWallThickness,
      height: shaftWallThickness
    },
    ...(rearOpening
      ? [
          {
            x: shaftLeft - shaftWallThickness,
            y: shaftTop - shaftWallThickness,
            width: rearOpening.start - shaftLeft + shaftWallThickness,
            height: shaftWallThickness
          },
          {
            x: rearOpening.end,
            y: shaftTop - shaftWallThickness,
            width: shaftRight - rearOpening.end + shaftWallThickness,
            height: shaftWallThickness
          }
        ]
      : [{
          x: shaftLeft - shaftWallThickness,
          y: shaftTop - shaftWallThickness,
          width: dimensions.ah + shaftWallThickness * 2,
          height: shaftWallThickness
        }])
  ].filter(rect => rect.width > 1 && rect.height > 1);
  const intersectsWithTolerance = (first, second, tolerance = 2) =>
    first.x < second.x + second.width - tolerance
    && first.x + first.width > second.x + tolerance
    && first.y < second.y + second.height - tolerance
    && first.y + first.height > second.y + tolerance;
  const isDoorWallCollision = pair =>
    [pair.carDoor, pair.landingDoor].some(door =>
      concreteCollisionRects.some(wall => intersectsWithTolerance(door, wall)));
  const concreteCutouts = [
    shaftRect,
    { x: frontOpening.start, y: shaftBottom, width: frontOpening.end - frontOpening.start, height: shaftWallThickness },
    ...(rearOpening
      ? [{ x: rearOpening.start, y: -shaftWallThickness, width: rearOpening.end - rearOpening.start, height: shaftWallThickness }]
      : [])
  ];
  const concretePath = [
    rectPath(shaftConcreteOuterRect),
    ...concreteCutouts.filter(rect => rect.width > 1 && rect.height > 1).map(rectPath)
  ].join(" ");
  const wallPath = [
    `M ${pathPoint(shaftLeft, shaftBottom)} L ${pathPoint(shaftLeft, shaftTop)}`,
    rearOpening
      ? `M ${pathPoint(shaftLeft, shaftTop)} L ${pathPoint(rearOpening.start, shaftTop)} M ${pathPoint(rearOpening.end, shaftTop)} L ${pathPoint(shaftRight, shaftTop)}`
      : `M ${pathPoint(shaftLeft, shaftTop)} L ${pathPoint(shaftRight, shaftTop)}`,
    `M ${pathPoint(shaftRight, shaftTop)} L ${pathPoint(shaftRight, shaftBottom)}`,
    `M ${pathPoint(shaftLeft, shaftBottom)} L ${pathPoint(frontOpening.start, shaftBottom)}`,
    `M ${pathPoint(frontOpening.end, shaftBottom)} L ${pathPoint(shaftRight, shaftBottom)}`
  ].join(" ");
  const cwtMarkup = cwtRect
    ? `<rect class="shaft-preview-svg__counterweight ${cwtCollision ? "shaft-preview-svg__counterweight--collision" : ""}" ${rectAttrs(cwtRect)} rx="3" />`
    : "";
  const doorMarkup = doorPairs.map(pair => {
    const doorCollisionClass = isDoorWallCollision(pair) ? "shaft-preview-svg__door--collision" : "";
    return `
      <rect class="shaft-preview-svg__landing-door ${doorCollisionClass}" ${rectAttrs(pair.landingDoor)} rx="${pillRadius}" />
      <rect class="shaft-preview-svg__car-door ${doorCollisionClass}" ${rectAttrs(pair.carDoor)} rx="${pillRadius}" />
    `;
  }).join("");
  const shoulderMarkup = doorPairs.map(pair => {
    const y = pair.side === "rear"
      ? cabinOuterRect.y
      : cabinOuterRect.y + cabinOuterRect.height;
    const outerLeft = cabinOuterRect.x;
    const outerRight = cabinOuterRect.x + cabinOuterRect.width;
    const opening = getCabinDoorOpeningBounds(pair);
    const minSegment = 20;
    const segments = [];

    if (opening.start - outerLeft > minSegment) {
      segments.push(`<line class="shaft-preview-svg__car-shoulder" ${lineAttrs(outerLeft, y, opening.start, y)} />`);
    }
    if (outerRight - opening.end > minSegment) {
      segments.push(`<line class="shaft-preview-svg__car-shoulder" ${lineAttrs(opening.end, y, outerRight, y)} />`);
    }

    return segments.join("");
  }).join("");
  const openingMarkerMarkup = doorPairs.map(pair => {
    const isRear = pair.side === "rear";
    const opening = getCabinDoorOpeningBounds(pair);
    const outerY = isRear ? cabinOuterRect.y : cabinOuterRect.y + cabinOuterRect.height;
    const innerY = isRear ? cabinInnerRect.y : cabinInnerRect.y + cabinInnerRect.height;

    return `
      <line class="shaft-preview-svg__door-opening-marker" ${lineAttrs(opening.start, outerY, opening.start, innerY)} />
      <line class="shaft-preview-svg__door-opening-marker" ${lineAttrs(opening.end, outerY, opening.end, innerY)} />
    `;
  }).join("");

  return `
    <svg class="shaft-preview-svg" viewBox="0 0 ${svgWidth} ${svgHeight}" role="img" aria-label="План шахты">
      <defs>
        <pattern id="shaftConcreteHatch" width="8" height="8" patternUnits="userSpaceOnUse" patternTransform="rotate(30)">
          <rect class="shaft-preview-svg__shaft-concrete-fill" width="8" height="8" />
          <line class="shaft-preview-svg__shaft-concrete-hatch" x1="0" y1="0" x2="0" y2="8" />
        </pattern>
      </defs>
      <rect class="shaft-preview-svg__shaft-fill" ${rectAttrs(shaftRect)} />
      <path class="shaft-preview-svg__shaft-concrete" d="${concretePath}" fill-rule="evenodd" />
      <path class="shaft-preview-svg__shaft" d="${wallPath}" />
      <line class="shaft-preview-svg__axis" x1="${mapX(shaftRect.x + shaftRect.width / 2).toFixed(1)}" y1="${mapY(shaftRect.y).toFixed(1)}" x2="${mapX(shaftRect.x + shaftRect.width / 2).toFixed(1)}" y2="${mapY(shaftRect.y + shaftRect.height).toFixed(1)}" />
      <line class="shaft-preview-svg__axis" x1="${mapX(shaftRect.x).toFixed(1)}" y1="${mapY(shaftRect.y + shaftRect.height / 2).toFixed(1)}" x2="${mapX(shaftRect.x + shaftRect.width).toFixed(1)}" y2="${mapY(shaftRect.y + shaftRect.height / 2).toFixed(1)}" />
      ${cwtMarkup}
      <rect class="shaft-preview-svg__car-outer ${cabinCollision ? "shaft-preview-svg__car--collision" : ""}" ${rectAttrs(cabinOuterRect)} rx="6" />
      <rect class="shaft-preview-svg__car-inner ${cabinCollision ? "shaft-preview-svg__car--collision" : ""}" ${rectAttrs(cabinInnerRect)} rx="5" />
      ${shoulderMarkup}
      ${openingMarkerMarkup}
      ${doorMarkup}
      <text class="shaft-preview-svg__label" x="${mapX(shaftRect.x + shaftRect.width / 2).toFixed(1)}" y="${mapY(shaftRect.y + shaftRect.height + shaftWallThickness + 130).toFixed(1)}">AH ${formatPreviewNumber(dimensions.ah)}</text>
      <text class="shaft-preview-svg__label shaft-preview-svg__label--vertical" x="${mapX(shaftRect.x - shaftWallThickness - 125).toFixed(1)}" y="${mapY(shaftRect.y + shaftRect.height / 2).toFixed(1)}">BH ${formatPreviewNumber(dimensions.bh)}</text>
    </svg>`;
}

function renderShaftPreviewMetrics(dimensions) {
  const metrics = [
    ["AH", dimensions.ah, "Ширина шахты"],
    ["BH", dimensions.bh, "Глубина шахты"],
    ["AA", dimensions.aa, "Ширина кабины"],
    ["BB", dimensions.bb, "Глубина кабины"],
    ["JJ", dimensions.jj, "Ширина дверей"],
    ...(dimensions.doorWidth && dimensions.doorWidth !== dimensions.jj
      ? [[dimensions.doorWidthMetricName, dimensions.doorWidth, dimensions.centerOpeningDoor ? "Расчетная ширина CO" : "Расчетная ширина 2S"]]
      : []),
    [
      dimensions.rearCwt ? "DK" : "KK",
      dimensions.rearCwt ? dimensions.dk : dimensions.kk,
      dimensions.rearCwt
        ? "От дальней стенки двери кабины до внутренней стенки кабины"
        : "Передняя стенка кабины"
    ],
    [dimensions.rearCwt ? "CJ" : "A4", dimensions.a4, "Эксцентриситет"],
    ["Тип", dimensions.rearCwt ? "Задний" : "Боковой", "Компоновка противовеса"],
    ...(dimensions.rearCwt && !dimensions.centerOpeningDoor
      ? [["Открывание", dimensions.rearDoorDirectionLabel, "Направление ТО-дверей"]]
      : dimensions.rearCwt
        ? []
        : [["Место", dimensions.cwtPlaceLabel, "Положение противовеса"]]),
    ...(dimensions.rearCwt && dimensions.cb
      ? [["CB", dimensions.cb, "От оси кабины до правой стены"]]
      : []),
    ...(dimensions.rearCwt && dimensions.carCenterY
      ? [["CC", dimensions.carCenterY, "От задней стены до оси кабины"]]
      : []),
    [dimensions.cwtMetricName, dimensions.cwtMetricValue, "Противовес по ширине"],
    [dimensions.cwtDepthLabel, dimensions.cwtDepth, "Противовес по глубине"],
    ...(dimensions.rearCwt && dimensions.cwtRearClearance !== null
      ? [["Зазор", dimensions.cwtRearClearance, "От задней стены до противовеса"]]
      : [])
  ];

  return metrics
    .filter(([, value]) => value !== null && value !== undefined && value !== "")
    .map(([name, value, label]) => `
      <div class="shaft-preview__metric">
        <dt>${escapeHtml(name)}</dt>
        <dd>${formatPreviewMetricValue(value)}<span>${escapeHtml(label)}</span></dd>
      </div>`)
    .join("");
}

function evaluateLevelExpression(expression, context) {
  if (!expression) return 1;

  const result = evaluateFormulaExpression(expression, context);
  const numericResult = Number(result);
  return Number.isFinite(numericResult) ? numericResult : -1;
}

function formatValidationValue(value) {
  if (typeof value === "number" && Number.isFinite(value)) {
    const rounded = Math.round(value * 1000) / 1000;
    return String(rounded);
  }

  return hasValue(value) ? String(value) : "";
}

function formatValidationMessage(message, context) {
  return String(message || "Параметры не проходят проверку T-FLEX.")
    .replace(/\{([^{}]+)\}/g, (_, expression) => {
      const value = evaluateFormulaExpression(expression, context);
      return hasValue(value) ? formatValidationValue(value) : `{${expression}}`;
    });
}

function stripStringLiterals(value) {
  return String(value || "").replace(/(["'])(?:\\.|(?!\1).)*\1/g, "");
}

function extractIdentifierTokens(value) {
  const tokens = new Set();
  const text = stripStringLiterals(value);
  for (const match of text.matchAll(/[$A-Za-z_][A-Za-z0-9_$]*/g)) {
    tokens.add(match[0]);
  }

  return [...tokens];
}

function extractMessageExpressionTokens(message) {
  const tokens = new Set();
  for (const match of String(message || "").matchAll(/\{([^{}]+)\}/g)) {
    for (const token of extractIdentifierTokens(match[1])) {
      tokens.add(token);
    }
  }

  return [...tokens];
}

function getTemplateDefinitionMap() {
  return new Map(getTemplateDefinitions().map(parameter => [parameter.name, parameter]));
}

function getParameterNameSet() {
  return new Set((state.selectedTemplate?.parameters || []).map(parameter => parameter.name));
}

function getDefinitionDependencyTokens(definition) {
  return [
    ...extractIdentifierTokens(definition.expression),
    ...extractIdentifierTokens(definition.levelExpression)
  ];
}

function resolveParameterDependencies(token, definitionMap, parameterNames, visited = new Set()) {
  if (parameterNames.has(token)) return [token];
  if (visited.has(token)) return [];
  visited.add(token);

  const definition = definitionMap.get(token);
  if (!definition) return [];

  const dependencies = new Set();
  for (const dependencyToken of getDefinitionDependencyTokens(definition)) {
    for (const dependency of resolveParameterDependencies(dependencyToken, definitionMap, parameterNames, visited)) {
      dependencies.add(dependency);
    }
  }

  return [...dependencies];
}

function getValidationFieldNames(rule) {
  if (Array.isArray(rule.fieldNames) && rule.fieldNames.length > 0) {
    return rule.fieldNames;
  }

  const parameterNames = getParameterNameSet();
  const definitionMap = getTemplateDefinitionMap();
  const messageFields = extractMessageExpressionTokens(rule.message)
    .filter(token => parameterNames.has(token));

  if (messageFields.length > 0) return [...new Set(messageFields)];

  const fields = new Set();
  const baseName = String(rule.name || "").replace(/^r_/, "");
  if (parameterNames.has(baseName)) fields.add(baseName);

  for (const token of extractIdentifierTokens(rule.expression)) {
    if (parameterNames.has(token)) {
      fields.add(token);
    }
  }

  if (fields.size > 0) return [...fields];

  for (const token of extractIdentifierTokens(rule.expression)) {
    for (const dependency of resolveParameterDependencies(token, definitionMap, parameterNames)) {
      fields.add(dependency);
    }
  }

  return [...fields];
}

function collectValidationFieldNames(errors = []) {
  return new Set(errors.flatMap(error => error.fieldNames || []));
}

function getShaftPreviewGeometryErrors(context) {
  if (!isLehyProTemplate() || getPreviewLayoutType() !== "rear") return [];

  const cc = getPreviewNumber(context, "CC");
  const bb = getPreviewNumber(context, "BB");
  const ww = getPreviewNumber(context, "WW") || getPreviewVariantNumber(context, "WW_", [
    "WW_1",
    "WW_11",
    "WW_12",
    "WW_2",
    "WW_21",
    "WW_22",
    "WW_3",
    "WW_31",
    "WW_32"
  ]);
  const entrances = Math.max(1, Math.round(getPreviewNumber(context, "NE") || 1));
  const rearWall = entrances > 1
    ? (getPreviewNumber(context, "DK") || 141)
    : (getPreviewNumber(context, "bb") || 30);

  if (!cc || !bb || !ww) return [];

  const availableDepth = cc - bb / 2 - rearWall;
  const requiredDepth = ww + 30;
  if (availableDepth >= requiredDepth) return [];

  const deficit = Math.max(1, Math.ceil(requiredDepth - availableDepth));
  return [{
    name: "rear_counterweight_clearance",
    message: `Недостаточно места для заднего противовеса. Требуется еще ${deficit} мм.`,
    fieldNames: ["CC", "BB", "WW", entrances > 1 ? "DK" : "bb", "NE"]
  }];
}

function applyValidationHighlights(errors = []) {
  state.validationFieldNames = collectValidationFieldNames(errors);

  for (const field of parametersForm.querySelectorAll(".field--invalid")) {
    field.classList.remove("field--invalid");
  }

  for (const input of parametersForm.querySelectorAll("input, select, textarea")) {
    input.classList.remove("is-invalid");
    input.removeAttribute("aria-invalid");

    const name = input.dataset.parameterName || input.name;
    if (!state.validationFieldNames.has(name)) continue;

    input.classList.add("is-invalid");
    input.setAttribute("aria-invalid", "true");
    input.closest(".field")?.classList.add("field--invalid");
  }
}

function getCurrentValidationIssues(context = buildLevelContext()) {
  const rules = state.selectedTemplate?.validationRules || [];
  const issues = [];
  const seenMessages = new Set();

  for (const rule of rules) {
    const result = evaluateFormulaExpression(rule.expression, context);
    if (isValidationPassed(result)) continue;

    const message = formatValidationMessage(rule.message, context);
    if (seenMessages.has(message)) continue;

    seenMessages.add(message);
    issues.push({
      name: rule.name,
      message,
      fieldNames: getValidationFieldNames(rule),
      severity: normalizeValidationSeverity(rule.severity)
    });
  }

  for (const error of getShaftPreviewGeometryErrors(context)) {
    if (seenMessages.has(error.message)) continue;
    seenMessages.add(error.message);
    issues.push({ ...error, severity: "error" });
  }

  return issues;
}

function appendValidationIssueGroup(titleText, issues, severity) {
  if (issues.length === 0) return;

  const group = document.createElement("section");
  group.className = `validation-panel__group validation-panel__group--${severity}`;

  const title = document.createElement("h3");
  title.className = "validation-panel__title";
  title.textContent = titleText;

  const list = document.createElement("ul");
  list.className = "validation-panel__list";

  for (const issue of issues) {
    const item = document.createElement("li");
    item.textContent = issue.message;
    list.append(item);
  }

  group.append(title, list);
  validationPanel.append(group);
}

function updateValidationPanel(issues = []) {
  validationPanel.replaceChildren();

  if (issues.length === 0) {
    validationPanel.hidden = true;
    return;
  }

  const { errors, warnings } = partitionValidationIssues(issues);
  validationPanel.classList.toggle("validation-panel--warning-only", errors.length === 0);
  appendValidationIssueGroup("Проверьте параметры", errors, "error");
  appendValidationIssueGroup("Предупреждения", warnings, "warning");
  validationPanel.hidden = false;
}

function isParameterVisible(parameter, context) {
  if (!parameter.levelExpression) return true;
  return evaluateLevelExpression(parameter.levelExpression, context) >= 0;
}

function isStopParameter(parameter) {
  const name = parameter.name;
  return STOP_CONTROL_NAMES.has(name)
    || /^s\d{2}_(name_1|level_1|front_1|rear_1|em_1)$/.test(name)
    || /^s_top_(name_1|level_1|rear_1)$/.test(name);
}

function isFrontendHiddenParameter(parameter) {
  return FRONTEND_HIDDEN_PARAMETER_NAMES.has(parameter.name);
}

function isLehyProStopLevelParameter(parameter, template = state.selectedTemplate) {
  return isLehyProTemplate(template)
    && /^s(?:\d{2}|_top)_level_1$/.test(parameter?.name || "");
}

function normalizeStopFloorName(value) {
  const number = Math.trunc(toNumber(value));
  return number === 0 ? 1 : number;
}

function getNextStopFloorName(value) {
  const next = value + 1;
  return next === 0 ? 1 : next;
}

function getAutomaticStopNameStart() {
  const firstStopName = getParameterDefinition("s01_name_1");
  if (!firstStopName) return 1;
  return normalizeStopFloorName(getParameterValue(firstStopName));
}

function getAutomaticStopName(index, start = 1) {
  let floor = normalizeStopFloorName(start);
  for (let position = 1; position < index; position += 1) {
    floor = getNextStopFloorName(floor);
  }

  return floor;
}

function getStopNameValue(index, stops, manualNames, automaticStart) {
  const parameterName = getStopNameParameterName(index, stops);
  if (manualNames) {
    const parameter = getParameterDefinition(parameterName);
    if (parameter) return normalizeStopFloorName(getParameterValue(parameter));
  }

  return getAutomaticStopName(index, automaticStart);
}

function findLobbyStopIndex(stops, manualNames, automaticStart) {
  for (let index = 1; index <= stops; index += 1) {
    if (getStopNameValue(index, stops, manualNames, automaticStart) === 1) {
      return index;
    }
  }

  return 1;
}

function synchronizeAutomaticStopState(stops) {
  const manualNames = toFlagNumber(getParameterValueByName("name")) === 1;
  const automaticStart = getAutomaticStopNameStart();

  if (!manualNames) {
    for (let index = 1; index <= stops; index += 1) {
      const parameter = getParameterDefinition(getStopNameParameterName(index, stops));
      if (parameter) {
        state.parameterValues[parameter.name] = getAutomaticStopName(index, automaticStart);
      }
    }
  }

  state.parameterValues.main_floor = resolveMainFloor({
    mainValue: getParameterValueByName("main"),
    selectedMainFloor: getParameterValueByName("main_floor"),
    lobbyStopIndex: findLobbyStopIndex(stops, manualNames, automaticStart),
    stops
  });
}

function getAutomaticStopLevel(index, stops) {
  return calculateAutomaticStopLevel({
    bottomLevel: getParameterValueByName("s01_level_1"),
    travelHeightMeters: getParameterValueByName("TR"),
    index,
    stops
  });
}

function synchronizeAutomaticStopLevels(stops) {
  const synchronizedValues = getAuthoritativeStopLevelValues({
    stops,
    manualLevels: getParameterValueByName("level"),
    values: state.parameterValues,
    bottomLevel: getParameterValueByName("s01_level_1"),
    travelHeightMeters: getParameterValueByName("TR"),
    hasParameter: name => Boolean(getParameterDefinition(name))
  });

  for (const [name, value] of Object.entries(synchronizedValues)) {
    if (hasValue(value)) state.parameterValues[name] = value;
  }
}

function createDisplayInput(value, parameterName = null) {
  const input = document.createElement("input");
  input.type = "number";
  input.value = value;
  input.disabled = true;
  input.className = "stops-table__input";
  if (parameterName) {
    input.dataset.parameterName = parameterName;
    input.name = parameterName;
  }
  return input;
}

function bindInputChange(input, parameter) {
  const handleInputChange = event => {
    const focusTarget = getInputFocusTarget(input);
    if (isLehyProStopLevelParameter(parameter) && event?.type === "input") {
      if (/^-?\d*$/.test(input.value)) {
        state.parameterValues[parameter.name] = input.value;
      } else {
        input.value = hasValue(state.parameterValues[parameter.name])
          ? state.parameterValues[parameter.name]
          : "";
      }
      return;
    }

    if (acceptsDecimalInput(parameter) && event?.type === "input") {
      state.parameterValues[parameter.name] = input.value;
      return;
    }

    state.parameterValues[parameter.name] = readInputValue(input, parameter);
    renderParametersAfterInputChange(focusTarget);
  };

  if (input.type !== "checkbox" && input.type !== "radio") {
    input.addEventListener("input", handleInputChange);
  }

  input.addEventListener("change", handleInputChange);
}

function createCompactInput(parameter, options = {}) {
  const input = document.createElement("input");
  const type = getParameterType(parameter);

  if (options.radioValue !== undefined) {
    input.type = "radio";
    input.name = parameter.name;
    input.value = String(options.radioValue);
  } else if (type === "bool" || type === "boolean") {
    input.type = "checkbox";
  } else if (isLehyProStopLevelParameter(parameter)) {
    input.type = "text";
    input.inputMode = "text";
    input.pattern = "-?[0-9]+";
    input.autocomplete = "off";
    input.spellcheck = false;
  } else if (acceptsDecimalInput(parameter)) {
    input.type = "text";
    input.inputMode = "decimal";
  } else {
    input.type = type === "number" || type === "integer" ? "number" : "text";
    if (type === "integer") input.step = "1";
    if (type === "number") input.step = "any";
  }

  input.dataset.parameterName = parameter.name;
  if (input.name === "") input.name = parameter.name;
  input.disabled = Boolean(parameter.isReadOnly);
  setInputValue(input, parameter, options.radioValue !== undefined ? getParameterValue(parameter) : getParameterValue(parameter));
  input.className = options.className || "stops-table__input";
  if (state.validationFieldNames.has(parameter.name)) {
    input.classList.add("is-invalid");
    input.setAttribute("aria-invalid", "true");
  }
  bindInputChange(input, parameter);
  return input;
}

function createStopModeHeader(name, labelText) {
  const label = document.createElement("label");
  label.className = "stops-table__mode";

  const parameter = getParameterDefinition(name);
  if (parameter) {
    label.append(createCompactInput(parameter, { className: "stops-table__mode-input" }));
  }

  const text = document.createElement("span");
  text.textContent = labelText;
  label.append(text);
  return label;
}

function createStopCellControl(name, fallback = "") {
  const parameter = getParameterDefinition(name);
  if (!parameter) {
    const span = document.createElement("span");
    span.className = "stops-table__empty";
    span.textContent = fallback;
    return span;
  }

  return createCompactInput(parameter);
}

function createStopRadio(value) {
  const parameter = getParameterDefinition("main_floor");
  if (!parameter) return document.createTextNode("");
  const input = document.createElement("input");
  input.type = "radio";
  input.name = parameter.name;
  input.value = String(value);
  input.dataset.parameterName = parameter.name;
  input.className = "stops-table__radio";
  input.disabled = Boolean(parameter.isReadOnly);
  input.checked = Number(getParameterValue(parameter)) === value;
  if (state.validationFieldNames.has(parameter.name)) {
    input.classList.add("is-invalid");
    input.setAttribute("aria-invalid", "true");
  }
  input.addEventListener("change", () => {
    if (!input.checked) return;
    const focusTarget = getInputFocusTarget(input);
    state.parameterValues[parameter.name] = value;
    renderParametersAfterInputChange(focusTarget);
  });
  return input;
}

function createDisplayStopRadio(checked) {
  const input = document.createElement("input");
  input.type = "radio";
  input.className = "stops-table__radio";
  input.disabled = true;
  input.checked = checked;
  return input;
}

function createStopsTable(context, options = {}) {
  const stops = clampStopCount(context.stops);
  synchronizeAutomaticStopState(stops);
  synchronizeAutomaticStopLevels(stops);

  const mainSelectionMode = getMainSelectionMode(getParameterValueByName("main"));
  const manualNames = toFlagNumber(context.name) === 1;
  const manualLevels = toFlagNumber(context.level) === 1;
  const hasRearDoors = toNumber(context.NE) === 2;
  const hasAo = toFlagNumber(context.em) === 1;
  const automaticStart = getAutomaticStopNameStart();
  const lobbyStopIndex = mainSelectionMode.manual
    ? toNumber(getParameterValueByName("main_floor"))
    : findLobbyStopIndex(stops, manualNames, automaticStart);

  const panel = document.createElement("section");
  panel.className = "stops-panel";

  if (!options.hideTitle) {
    const title = document.createElement("h3");
    title.textContent = STOP_GROUP_LABEL;
    panel.append(title);
  }

  const wrapper = document.createElement("div");
  wrapper.className = "stops-table-wrap";

  const table = document.createElement("table");
  table.className = "stops-table";

  const thead = document.createElement("thead");
  const headRow = document.createElement("tr");
  const headers = [
    createStopModeHeader("main", STOP_LOBBY_LABEL),
    createStopModeHeader("name", STOP_FLOOR_LABEL),
    createStopModeHeader("level", STOP_LEVEL_LABEL)
  ];

  if (hasRearDoors) {
    headers.push(document.createTextNode(STOP_FRONT_LABEL));
    headers.push(document.createTextNode(STOP_REAR_LABEL));
  }

  if (hasAo) {
    headers.push(document.createTextNode(STOP_AO_LABEL));
  }

  for (const header of headers) {
    const th = document.createElement("th");
    th.append(header);
    headRow.append(th);
  }

  thead.append(headRow);
  table.append(thead);

  const tbody = document.createElement("tbody");
  for (let index = 1; index <= stops; index += 1) {
    const rowKey = getStopRowKey(index, stops);
    const row = document.createElement("tr");

    const lobbyCell = document.createElement("td");
    lobbyCell.append(mainSelectionMode.radiosReadOnly
      ? createDisplayStopRadio(index === lobbyStopIndex)
      : createStopRadio(index));
    row.append(lobbyCell);

    const stopNameParameterName = getStopNameParameterName(index, stops);
    const nameCell = document.createElement("td");
    nameCell.append(manualNames
      ? createStopCellControl(stopNameParameterName)
      : createDisplayInput(getAutomaticStopName(index, automaticStart), stopNameParameterName));
    row.append(nameCell);

    const stopLevelParameterName = getStopLevelParameterName(index, stops);
    const levelCell = document.createElement("td");
    levelCell.append(manualLevels && rowKey !== "s_top"
      ? createStopCellControl(stopLevelParameterName)
      : createDisplayInput(getAutomaticStopLevel(index, stops), stopLevelParameterName));
    row.append(levelCell);

    if (hasRearDoors) {
      const frontCell = document.createElement("td");
      if (rowKey !== "s_top") frontCell.append(createStopCellControl(`${rowKey}_front_1`));
      row.append(frontCell);

      const rearCell = document.createElement("td");
      rearCell.append(createStopCellControl(rowKey === "s_top" ? "s_top_rear_1" : `${rowKey}_rear_1`));
      row.append(rearCell);
    }

    if (hasAo) {
      const aoCell = document.createElement("td");
      if (rowKey !== "s_top" && index > 1) aoCell.append(createStopCellControl(`${rowKey}_em_1`));
      row.append(aoCell);
    }

    tbody.append(row);
  }

  table.append(tbody);
  wrapper.append(table);
  panel.append(wrapper);
  return panel;
}

function getParameterDisplayParts(parameter) {
  const displayName = normalizeParameterDisplayText(parameter.displayName || parameter.name);
  const explicitCategory = normalizeParameterCategory(
    parameter.category || parameter.groupName || parameter.group || "");
  const separatorIndex = displayName.indexOf("/");

  if (separatorIndex < 0) {
    return {
      category: explicitCategory || DEFAULT_PARAMETER_CATEGORY,
      label: normalizeParameterLabel(displayName, parameter.name)
    };
  }

  const category = displayName.slice(0, separatorIndex);
  const label = displayName.slice(separatorIndex + 1);

  return {
    category: normalizeParameterCategory(category) || explicitCategory || DEFAULT_PARAMETER_CATEGORY,
    label: normalizeParameterLabel(label, parameter.name)
  };
}

function normalizeParameterDisplayText(value) {
  return String(value || "")
    .replace(/\s+/g, " ")
    .trim();
}

function isBrokenParameterText(value) {
  const text = normalizeParameterDisplayText(value);
  return !text || /^[?\-_\s]+$/.test(text);
}

function normalizeParameterCategory(value) {
  const category = normalizeParameterDisplayText(value);
  if (isBrokenParameterText(category)) return "";
  return CATEGORY_LABEL_OVERRIDES.get(category) || category;
}

function normalizeParameterLabel(value, fallbackName) {
  const label = normalizeParameterDisplayText(value);
  if (!isBrokenParameterText(label)) {
    return FIELD_LABEL_OVERRIDES.get(label) || label;
  }

  return FIELD_LABEL_OVERRIDES.get(fallbackName) || fallbackName;
}

function getFieldLabelText(parameter) {
  const parts = getParameterDisplayParts(parameter);
  return parts.label;
}

function createParameterGroup(category) {
  const group = document.createElement("fieldset");
  group.className = "parameter-group";
  group.dataset.category = category;

  if (category === STOP_GROUP_LABEL) {
    group.classList.add("parameter-group--stops");
  }

  const legend = document.createElement("legend");
  legend.className = "parameter-group__title";
  legend.textContent = category;

  const fields = document.createElement("div");
  fields.className = "parameter-group__fields";

  group.append(legend, fields);
  return group;
}

function appendToParameterGroup(groups, category, node) {
  let group = groups.get(category);
  if (!group) {
    group = createParameterGroup(category);
    groups.set(category, group);
    parametersForm.append(group);
  }

  group.querySelector(".parameter-group__fields").append(node);
}

function getCategoryDisplayOrder(category, fallbackIndex) {
  const index = CATEGORY_DISPLAY_ORDER.indexOf(category);
  return index >= 0 ? index : CATEGORY_DISPLAY_ORDER.length + fallbackIndex;
}

function reorderParameterGroups(groups) {
  [...groups.entries()]
    .map(([category, group], index) => ({
      category,
      group,
      index,
      order: getCategoryDisplayOrder(category, index)
    }))
    .sort((left, right) => left.order - right.order || left.index - right.index)
    .forEach(({ group }) => parametersForm.append(group));
}

function getRenderedParameterCategories() {
  return [...parametersForm.querySelectorAll(".parameter-group")]
    .map(group => group.dataset.category)
    .filter(Boolean);
}

function getParameterGroupByCategory(category) {
  return [...parametersForm.querySelectorAll(".parameter-group")]
    .find(group => group.dataset.category === category);
}

function prefersReducedMotion() {
  return window.matchMedia?.("(prefers-reduced-motion: reduce)")?.matches || false;
}

function scrollToParameterCategory(category) {
  const group = getParameterGroupByCategory(category);
  if (!group) return;

  group.scrollIntoView({
    behavior: prefersReducedMotion() ? "auto" : "smooth",
    block: "start",
    inline: "nearest"
  });
}

function applyParameterTabVisibility() {
  const categories = getRenderedParameterCategories();
  if (!categories.length) {
    if (parameterTabs) parameterTabs.replaceChildren();
    state.activeParameterCategory = null;
    return;
  }

  if (!state.activeParameterCategory || !categories.includes(state.activeParameterCategory)) {
    state.activeParameterCategory = categories[0];
  }

  for (const group of parametersForm.querySelectorAll(".parameter-group")) {
    group.hidden = !state.showAllParameters && group.dataset.category !== state.activeParameterCategory;
  }

  for (const button of parameterTabs?.querySelectorAll("button[data-category]") || []) {
    const active = button.dataset.category === state.activeParameterCategory;
    button.classList.toggle("is-active", active);
    button.setAttribute("aria-pressed", active ? "true" : "false");
  }

  if (showAllParametersToggle) {
    showAllParametersToggle.checked = state.showAllParameters;
  }
}

function renderParameterTabs() {
  if (!parameterTabs) return;

  const categories = getRenderedParameterCategories();
  const visibleCategories = categories;

  if (state.activeParameterCategory && !visibleCategories.includes(state.activeParameterCategory)) {
    state.activeParameterCategory = visibleCategories[0] || categories[0] || null;
  }

  parameterTabs.replaceChildren();
  for (const category of visibleCategories) {
    const button = document.createElement("button");
    button.type = "button";
    button.dataset.category = category;
    button.textContent = category;
    button.addEventListener("click", () => {
      state.activeParameterCategory = category;
      applyParameterTabVisibility();
      if (state.showAllParameters) {
        requestAnimationFrame(() => scrollToParameterCategory(category));
      }
    });
    parameterTabs.append(button);
  }

  applyParameterTabVisibility();
}

function getCurrentParameterInputValue(parameter, context) {
  return normalizeValueForAllowedList(
    parameter,
    getDisplayParameterValue(parameter, context),
    context);
}

function wireParameterInput(input, parameter, isDisabled) {
  input.name = parameter.name;
  input.dataset.parameterName = parameter.name;
  input.disabled = isDisabled;
  input.required = Boolean(parameter.isRequired) && !input.disabled;
  const handleParameterChange = event => {
    const focusTarget = getInputFocusTarget(input);
    if (acceptsDecimalInput(parameter) && event?.type === "input") {
      state.parameterValues[parameter.name] = input.value;
      return;
    }

    state.parameterValues[parameter.name] = readInputValue(input, parameter);
    renderParametersAfterInputChange(focusTarget);
  };

  if (input.tagName === "INPUT" || input.tagName === "TEXTAREA") {
    input.addEventListener("input", handleParameterChange);
  }

  input.addEventListener("change", handleParameterChange);
}

function createParameterInput(parameter, context) {
  const field = document.createElement("div");
  field.className = "field";
  const hasValidationError = state.validationFieldNames.has(parameter.name);
  if (hasValidationError) {
    field.classList.add("field--invalid");
  }

  const label = document.createElement("span");
  label.className = "field__label";
  label.textContent = getFieldLabelText(parameter);

  let input;
  const type = getParameterType(parameter);
  const isDisabled = Boolean(parameter.isReadOnly);
  const allowedValues = getAllowedValues(parameter, context);
  const currentValue = getCurrentParameterInputValue(parameter, context);
  const displayName = parameter.displayName || parameter.name;
  const isAddressMultiline = displayName.toLowerCase().includes("\u0430\u0434\u0440\u0435\u0441");
  const isMultiline = parameter.multiline || parameter.name === "$Address" || displayName.toLowerCase().includes("адрес");

  if (type === "bool" || type === "boolean") {
    field.classList.add("field--checkbox");
    input = document.createElement("input");
    input.type = "checkbox";
  } else if (isMultiline || isAddressMultiline) {
    input = document.createElement("textarea");
    input.rows = parameter.rows || 3;
  } else if (allowedValues.length) {
    input = document.createElement("select");
    for (const value of allowedValues) {
      const option = document.createElement("option");
      option.value = value;
      option.textContent = getAllowedValueLabel(parameter, value);
      input.append(option);
    }
  } else if (acceptsDecimalInput(parameter)) {
    input = document.createElement("input");
    input.type = "text";
    input.inputMode = "decimal";
  } else {
    input = document.createElement("input");
    input.type = type === "number" || type === "integer" ? "number" : "text";
    if (type === "integer") input.step = "1";
    if (type === "number") input.step = "any";
    if (parameter.minValue !== null && parameter.minValue !== undefined) input.min = parameter.minValue;
    if (parameter.maxValue !== null && parameter.maxValue !== undefined) input.max = parameter.maxValue;
  }

  wireParameterInput(input, parameter, isDisabled);
  setInputValue(input, parameter, currentValue);
  if (hasValidationError) {
    input.classList.add("is-invalid");
    input.setAttribute("aria-invalid", "true");
  }

  const shouldShowUnit = parameter.unit && input.tagName !== "SELECT" && input.type !== "checkbox" && input.type !== "radio";
  if (shouldShowUnit) {
    const control = document.createElement("div");
    control.className = "field__control";
    const suffix = document.createElement("span");
    suffix.className = "field__suffix";
    suffix.textContent = parameter.unit;
    control.append(input, suffix);
    field.append(label, control);
  } else {
    field.append(label, input);
  }

  if (parameter.multiline || parameter.name === "$Address" || input.tagName === "TEXTAREA") {
    field.classList.add("field--wide");
  }

  return field;
}

function getInputFocusTarget(input) {
  const name = input?.dataset?.parameterName || input?.name || null;
  if (!name) return null;

  return {
    name,
    type: input.type,
    value: input.type === "radio" ? input.value : null
  };
}

function getFocusedParameterTarget() {
  const activeElement = document.activeElement;
  if (!activeElement || !parametersForm.contains(activeElement)) return null;
  return getInputFocusTarget(activeElement);
}

function focusParameterInput(focusTarget) {
  if (!focusTarget?.name) return;

  const inputs = [...parametersForm.querySelectorAll("input, select, textarea")]
    .filter(element => (element.dataset.parameterName || element.name) === focusTarget.name && !element.disabled);
  const input = inputs.find(element => focusTarget.type !== "radio" || element.value === focusTarget.value)
    || inputs[0];

  input?.focus({ preventScroll: true });
}

function getViewportScrollPosition() {
  const scrollingElement = document.scrollingElement || document.documentElement;
  return {
    x: window.scrollX,
    y: window.scrollY,
    elementLeft: scrollingElement.scrollLeft,
    elementTop: scrollingElement.scrollTop
  };
}

function applyViewportScrollPosition(scrollPosition) {
  if (!scrollPosition) return;

  const scrollingElement = document.scrollingElement || document.documentElement;
  scrollingElement.scrollLeft = scrollPosition.elementLeft;
  scrollingElement.scrollTop = scrollPosition.elementTop;
  window.scrollTo({
    left: scrollPosition.x,
    top: scrollPosition.y,
    behavior: "auto"
  });
}

function restoreViewport(options, scrollPosition, focusTarget) {
  if (!options.preserveScroll && !options.preserveFocus) return;

  let focusRestored = false;
  const restore = () => {
    if (options.preserveFocus && !focusRestored) {
      focusParameterInput(focusTarget);
      focusRestored = true;
    }

    if (options.preserveScroll) {
      applyViewportScrollPosition(scrollPosition);
    }
  };

  restore();
  requestAnimationFrame(() => {
    restore();
    requestAnimationFrame(restore);
  });
}

function renderParametersAfterInputChange(focusTarget) {
  state.pendingFocusTarget = focusTarget || state.pendingFocusTarget;
  if (state.pendingRenderFrame !== null) {
    cancelAnimationFrame(state.pendingRenderFrame);
  }

  state.pendingRenderFrame = requestAnimationFrame(() => {
    const nextFocusTarget = state.pendingFocusTarget;
    state.pendingRenderFrame = null;
    state.pendingFocusTarget = null;
    renderParameters({ preserveScroll: true, preserveFocus: true, focusTarget: nextFocusTarget });
  });
}

function renderParameters(options = {}) {
  const scrollPosition = options.preserveScroll
    ? getViewportScrollPosition()
    : null;
  const focusTarget = options.preserveFocus ? (options.focusTarget || getFocusedParameterTarget()) : null;

  rememberCurrentValues();
  parametersForm.replaceChildren();
  if (!state.selectedTemplate) {
    state.validationFieldNames = new Set();
    updateValidationPanel();
    if (parameterReadyBanner) parameterReadyBanner.hidden = true;
    updateShaftPreview(null);
    restoreViewport(options, scrollPosition, focusTarget);
    return;
  }

  const context = buildLevelContext();
  const validationIssues = getCurrentValidationIssues(context);
  const validationErrors = validationIssues.filter(isBlockingValidationIssue);
  state.validationFieldNames = collectValidationFieldNames(validationErrors);
  let stopsTablePending = false;
  const groups = new Map();
  for (const parameter of state.selectedTemplate.parameters) {
    if (isFrontendHiddenParameter(parameter)) continue;
    if (isStopParameter(parameter)) continue;

    if (isParameterVisible(parameter, context)) {
      const parts = getParameterDisplayParts(parameter);
      appendToParameterGroup(groups, parts.category, createParameterInput(parameter, context));
      if (parameter.name === "stops") {
        stopsTablePending = true;
      } else if (parameter.name === "em" && stopsTablePending) {
        appendToParameterGroup(groups, STOP_GROUP_LABEL, createStopsTable(context, { hideTitle: true }));
        stopsTablePending = false;
      }
    }
  }

  if (stopsTablePending) {
    appendToParameterGroup(groups, STOP_GROUP_LABEL, createStopsTable(context, { hideTitle: true }));
  }

  reorderParameterGroups(groups);
  renderParameterTabs();
  updateValidationPanel(validationIssues);
  if (parameterReadyBanner) {
    parameterReadyBanner.hidden = validationErrors.length > 0;
  }
  updateConfigurationNamePreview();
  updateShaftPreview(context);
  restoreViewport(options, scrollPosition, focusTarget);
}

function renderSelectedTemplate() {
  state.selectedTemplate = state.templates.find(template => template.id === templateSelect.value);
  formatSelect.replaceChildren();
  parametersForm.replaceChildren();
  state.validationFieldNames = new Set();
  state.activeParameterCategory = null;
  updateValidationPanel();
  updateConfigurationNamePreview();
  initializeParameterValues();

  if (!state.selectedTemplate) {
    updateShaftPreview(null);
    return;
  }

  for (const format of state.selectedTemplate.outputFormats) {
    const option = document.createElement("option");
    option.value = format;
    option.textContent = format.toUpperCase();
    formatSelect.append(option);
  }

  renderParameters();
  updateDownloadResultButton();
}

function collectParameters() {
  rememberCurrentValues();
  const parameters = {};
  for (const input of parametersForm.querySelectorAll("input, select, textarea")) {
    if (input.type === "radio" && !input.checked) continue;

    const name = input.dataset.parameterName || input.name;
    const definition = state.selectedTemplate.parameters.find(parameter => parameter.name === name);
    if (!definition) continue;
    if (input.disabled && !definition.submitWhenDisabled) continue;

    putCollectedParameter(parameters, definition, readInputValue(input, definition));
  }

  appendStopParameters(parameters);
  appendFrontendHiddenParameters(parameters);
  return parameters;
}

function putCollectedParameter(parameters, definition, value) {
  const type = getParameterType(definition);
  if (type === "number" || acceptsDecimalInput(definition)) {
    parameters[definition.name] = parseDecimalValue(value);
  } else if (type === "integer") {
    parameters[definition.name] = value === "" || value === null ? null : Number.parseInt(value, 10);
  } else if (type === "bool" || type === "boolean") {
    parameters[definition.name] = Boolean(value);
  } else {
    parameters[definition.name] = hasValue(value) ? String(value) : "";
  }
}

function appendStopParameters(parameters) {
  if (!state.selectedTemplate || !getParameterDefinition("stops")) return;

  const stops = clampStopCount(parameters.stops ?? getParameterValueByName("stops"));
  synchronizeAutomaticStopState(stops);
  synchronizeAutomaticStopLevels(stops);

  const mainFloor = getParameterDefinition("main_floor");
  if (mainFloor) {
    putCollectedParameter(parameters, mainFloor, state.parameterValues.main_floor);
  }

  const stopValues = collectStopParameterValues({
    stops,
    values: state.parameterValues,
    hasParameter: name => Boolean(getParameterDefinition(name))
  });
  for (const [name, value] of Object.entries(stopValues)) {
    putCollectedParameter(parameters, getParameterDefinition(name), value);
  }
}

function appendFrontendHiddenParameters(parameters) {
  if (!state.selectedTemplate) return;

  for (const definition of state.selectedTemplate.parameters) {
    if (!isFrontendHiddenParameter(definition)) continue;
    putCollectedParameter(parameters, definition, getParameterValue(definition));
  }
}

function renderResultFileActions(files, compact = false) {
  return files.map(file => `
    <span class="result-file-actions">
      <a href="${escapeHtml(file.downloadUrl)}">${escapeHtml(compact ? "скачать" : file.fileName)}</a>
      ${isPdfFile(file) ? `
        <button
          class="result-file-preview"
          type="button"
          data-preview-url="${escapeHtml(file.downloadUrl)}"
          data-preview-name="${escapeHtml(file.fileName || "drawing.pdf")}"
          data-preview-format="${escapeHtml(file.format || "pdf")}">Просмотреть</button>
      ` : ""}
    </span>
  `).join(" ");
}

function openPreviewFromControl(control) {
  if (!control?.dataset.previewUrl) return;
  openGeneratedFilePreview({
    downloadUrl: control.dataset.previewUrl,
    fileName: control.dataset.previewName,
    format: control.dataset.previewFormat
  }, control);
}

function renderJob(job) {
  state.latestJob = job;
  const files = job.resultFiles || [];
  const downloadLinks = renderResultFileActions(files);

  statusPanel.className = "job-status";
  statusPanel.innerHTML = `
    <div class="status ${escapeHtml(job.status.toLowerCase())}">${escapeHtml(job.status)}</div>
    <dl>
      <dt>Задание</dt><dd>${escapeHtml(job.id)}</dd>
      <dt>Шаблон</dt><dd>${escapeHtml(job.templateId)}</dd>
      <dt>Создано</dt><dd>${formatDate(job.createdAt)}</dd>
      <dt>Завершено</dt><dd>${formatDate(job.finishedAt)}</dd>
      <dt>Ошибка</dt><dd>${escapeHtml(job.errorMessage || "")}</dd>
      <dt>Результат</dt><dd>${downloadLinks || ""}</dd>
    </dl>
  `;
  updateDownloadResultButton(job);
}

function renderStatusError(messages) {
  state.latestJob = null;
  updateDownloadResultButton(null);
  statusPanel.className = "";
  const error = document.createElement("div");
  error.className = "error";

  for (const message of messages) {
    const line = document.createElement("div");
    line.textContent = message;
    error.append(line);
  }

  statusPanel.replaceChildren(error);
}

async function refreshJob(jobId) {
  const response = await apiFetch(`/api/jobs/${jobId}`);
  if (!response.ok) return;
  const job = await sessionRequests.readJson(response);
  if (job === sessionRequests.stalePayload) return;
  renderJob(job);

  if (job.status === "Completed" || job.status === "Failed" || job.status === "Cancelled") {
    clearInterval(state.pollTimer);
    state.pollTimer = null;
    await refreshJobs();
  }
}

async function refreshJobs() {
  const response = await apiFetch("/api/jobs?take=20");
  if (!response.ok) return;
  const jobs = await sessionRequests.readJson(response);
  if (jobs === sessionRequests.stalePayload) return;
  state.jobs = jobs;
  renderJobs();
}

function renderJobs() {
  jobsTableBody.replaceChildren();
  const query = getEditorSearchQuery();
  const jobs = state.jobs.filter(job => matchesJobSearch(job, query));

  if (state.jobs.length > 0 && jobs.length === 0) {
    const row = document.createElement("tr");
    row.innerHTML = `<td colspan="6">По этому запросу задания не найдены.</td>`;
    jobsTableBody.append(row);
    return;
  }

  for (const job of jobs) {
    const row = document.createElement("tr");
    const files = job.resultFiles || [];
    row.innerHTML = `
      <td>${escapeHtml(job.id.slice(0, 8))}</td>
      <td>${escapeHtml(job.templateId)}</td>
      <td><span class="status ${escapeHtml(job.status.toLowerCase())}">${escapeHtml(job.status)}</span></td>
      <td>${escapeHtml(job.outputFormat.toUpperCase())}</td>
      <td>${formatDate(job.createdAt)}</td>
      <td>${renderResultFileActions(files, true)}</td>
    `;
    jobsTableBody.append(row);
  }
}

async function submitJob(event) {
  event.preventDefault();
  if (!state.selectedTemplate) return;
  if (!canCreateJobs()) {
    renderStatusError([t("Недостаточно прав для создания задания.")]);
    return;
  }

  rememberCurrentValues();
  const validationIssues = getCurrentValidationIssues();
  const validationErrors = validationIssues.filter(isBlockingValidationIssue);
  updateValidationPanel(validationIssues);
  applyValidationHighlights(validationErrors);
  if (validationErrors.length > 0) {
    renderStatusError([t("Исправьте параметры перед созданием задания.")]);
    validationPanel.scrollIntoView({ behavior: "auto", block: "nearest" });
    return;
  }

  submitButton.disabled = true;
  state.latestJob = null;
  updateDownloadResultButton(null);
  statusPanel.className = "job-status";
  statusPanel.innerHTML = `<div class="status pending">Pending</div>`;

  try {
    const response = await apiFetch("/api/jobs", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        templateId: state.selectedTemplate.id,
        outputFormat: formatSelect.value,
        parameters: collectParameters()
      })
    });

    if (!response.ok) {
      renderStatusError(await readProblem(response, "Ошибка создания задания"));
      return;
    }

    const job = await sessionRequests.readJson(response);
    if (job === sessionRequests.stalePayload) return;
    state.activeJobId = job.id;
    renderJob(job);
    await refreshJobs();
    if (!sessionRequests.isCurrent(response)) return;

    clearInterval(state.pollTimer);
    state.pollTimer = setInterval(() => refreshJob(job.id), 1200);
  } catch {
    renderStatusError([t("Не удалось создать задание. Проверьте соединение с API.")]);
  } finally {
    submitButton.disabled = false;
  }
}

function resetJobForm(event) {
  event.preventDefault();
  state.editingConfigurationId = null;
  initializeParameterValues();
  renderParameters();
}

async function loadTemplates() {
  const response = await apiFetch("/api/templates");
  if (!response.ok) return;
  const templates = await sessionRequests.readJson(response);
  if (templates === sessionRequests.stalePayload) return;
  state.templates = templates;

  templateSelect.replaceChildren();
  for (const template of state.templates) {
    const option = document.createElement("option");
    option.value = template.id;
    option.textContent = template.name || template.code;
    templateSelect.append(option);
  }

  renderSelectedTemplate();
  applyEditorSearch();
}

async function loadProjects(selectedProjectId = null) {
  const response = await apiFetch("/api/projects");
  if (!response.ok) return;

  const previousValue = selectedProjectId || projectSelect.value;
  const projects = await sessionRequests.readJson(response);
  if (projects === sessionRequests.stalePayload) return;
  state.projects = projects;
  projectSelect.replaceChildren();

  if (state.projects.length === 0) {
    const option = document.createElement("option");
    option.value = "";
    option.textContent = "Создайте проект в ЛК";
    projectSelect.append(option);
    projectSelect.disabled = true;
    saveConfigurationButton.disabled = true;
    applyEditorSearch();
    return;
  }

  for (const project of state.projects) {
    const option = document.createElement("option");
    option.value = project.id;
    option.textContent = getProjectOptionLabel(project);
    projectSelect.append(option);
  }

  if (previousValue && state.projects.some(project => project.id === previousValue)) {
    projectSelect.value = previousValue;
  }

  projectSelect.disabled = false;
  saveConfigurationButton.disabled = false;
  applyEditorSearch();
}

async function saveCurrentConfiguration() {
  if (!state.selectedTemplate) return;
  if (!projectSelect.value) {
    renderStatusError([t("Сначала создайте проект в личном кабинете.")]);
    return;
  }

  const parameters = collectParameters();
  const name = getConfigurationName(parameters);
  const editingConfigurationId = state.editingConfigurationId;
  const url = editingConfigurationId
    ? `/api/project-configurations/${encodeURIComponent(editingConfigurationId)}`
    : `/api/projects/${projectSelect.value}/configurations`;
  const response = await apiFetch(url, {
    method: editingConfigurationId ? "PUT" : "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      name,
      templateId: state.selectedTemplate.id,
      outputFormat: formatSelect.value,
      parameters
    })
  });

  if (!response.ok) {
    renderStatusError(await readProblem(response, "Не удалось сохранить конфигурацию"));
    return;
  }

  const savedConfiguration = await sessionRequests.readJson(response);
  if (savedConfiguration === sessionRequests.stalePayload) return;
  state.editingConfigurationId = savedConfiguration.id || editingConfigurationId;
  updateConfigurationNamePreview(parameters);
  statusPanel.className = "empty";
  statusPanel.textContent = editingConfigurationId
    ? "Конфигурация обновлена"
    : "Конфигурация сохранена в проект";
  updateDownloadResultButton(null);
}

function applyConfiguration(configuration) {
  const template = state.templates.find(item => item.id === configuration.templateId);
  if (!template) {
    renderStatusError(["Шаблон этой конфигурации сейчас недоступен."]);
    return;
  }

  state.editingConfigurationId = configuration.id;
  templateSelect.value = template.id;
  renderSelectedTemplate();
  state.parameterValues = {
    ...state.parameterValues,
    ...(configuration.parameters || {})
  };
  if ([...formatSelect.options].some(option => option.value === configuration.outputFormat)) {
    formatSelect.value = configuration.outputFormat;
  }
  renderParameters();
  statusPanel.className = "empty";
  statusPanel.textContent = "Конфигурация загружена";
}

async function loadConfigurationFromUrl() {
  const configurationId = new URLSearchParams(window.location.search).get("configurationId");
  if (!configurationId) return;

  const response = await apiFetch(`/api/project-configurations/${encodeURIComponent(configurationId)}`);
  if (!response.ok) {
    renderStatusError(await readProblem(response, "Не удалось открыть конфигурацию"));
    return;
  }

  const configuration = await sessionRequests.readJson(response);
  if (configuration === sessionRequests.stalePayload) return;
  await loadProjects(configuration.projectId);
  if (!sessionRequests.isCurrent(response)) return;
  applyConfiguration(configuration);
}

async function loadCurrentUser() {
  const response = await apiFetch("/api/auth/me");
  if (!response.ok) {
    state.currentUser = null;
    updateAuthView();
    return false;
  }

  const currentUser = await sessionRequests.readJson(response);
  if (currentUser === sessionRequests.stalePayload) return false;
  state.currentUser = currentUser;
  updateAuthView();
  return isAuthenticated();
}

async function register(event) {
  event.preventDefault();
  registerStatus.hidden = true;
  registerStatus.textContent = "";

  const response = await apiFetch("/api/auth/register", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      userName: registerUserName.value,
      displayName: registerDisplayName.value,
      password: registerPassword.value
    })
  });

  if (!response.ok) {
    const messages = await readProblem(response, t("Не удалось отправить заявку"));
    registerStatus.hidden = false;
    registerStatus.className = "error";
    registerStatus.textContent = messages.join(" ");
    return;
  }

  registerForm.reset();
  registerStatus.hidden = false;
  registerStatus.className = "empty";
  registerStatus.textContent = t("Заявка отправлена. Доступ появится после подтверждения администратором.");
}

async function login(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const userNameInput = form.querySelector("[name='userName']") || loginUserName;
  const passwordInput = form.querySelector("[name='password']") || loginPassword;
  passwordInput.setCustomValidity("");

  const response = await apiFetch("/api/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      userName: userNameInput.value,
      password: passwordInput.value
    })
  });

  if (!response.ok) {
    passwordInput.setCustomValidity(t("Неверный логин или пароль"));
    passwordInput.reportValidity();
    return;
  }

  const currentUser = await sessionRequests.readJson(response);
  if (currentUser === sessionRequests.stalePayload) return;
  clearEditorSessionState();
  state.currentUser = currentUser;
  passwordInput.value = "";
  updateAuthView();
  await loadTemplates();
  await loadProjects();
  await refreshJobs();
  await loadConfigurationFromUrl();
}

async function logout() {
  clearEditorSessionState();
  state.currentUser = null;
  updateAuthView();
  await apiFetch("/api/auth/logout", { method: "POST" });
}

async function boot() {
  try {
    const authenticated = await loadCurrentUser();
    if (!authenticated) return;

    await loadTemplates();
    await loadProjects();
    await refreshJobs();
    await loadConfigurationFromUrl();
  } finally {
    hidePageSkeleton();
  }
}

templateSelect.addEventListener("change", renderSelectedTemplate);
formatSelect.addEventListener("change", () => updateDownloadResultButton());
globalSearchInput?.addEventListener("input", applyEditorSearch);
globalSearchInput?.addEventListener("keydown", event => {
  if (event.key !== "Escape") return;
  globalSearchInput.value = "";
  applyEditorSearch();
});
document.querySelector("#jobForm").addEventListener("submit", submitJob);
document.querySelector("#jobForm").addEventListener("reset", resetJobForm);
registerForm.addEventListener("submit", register);
loginForm.addEventListener("submit", login);
guestLoginForm?.addEventListener("submit", login);
showRegisterPanelButton?.addEventListener("click", () => showAuthPanel("register"));
showLoginPanelButton?.addEventListener("click", () => showAuthPanel("login"));
logoutButton.addEventListener("click", logout);
downloadResultButton?.addEventListener("click", () => {
  const url = downloadResultButton.dataset.downloadUrl;
  if (!url) return;
  window.location.href = url;
});
previewResultButton?.addEventListener("click", () => {
  openGeneratedFilePreview({
    downloadUrl: previewResultButton.dataset.downloadUrl,
    fileName: previewResultButton.dataset.fileName,
    format: previewResultButton.dataset.format
  }, previewResultButton);
});
for (const container of [statusPanel, jobsTableBody]) {
  container?.addEventListener("click", event => {
    const control = event.target.closest("[data-preview-url]");
    if (control) openPreviewFromControl(control);
  });
}
saveConfigurationButton.addEventListener("click", saveCurrentConfiguration);
showAllParametersToggle?.addEventListener("change", event => {
  state.showAllParameters = event.currentTarget.checked;
  applyParameterTabVisibility();
});
window.addEventListener("tflex:languagechange", () => {
  if (state.latestJob) renderJob(state.latestJob);
  renderJobs();
  updateDownloadResultButton();
});

await boot();
