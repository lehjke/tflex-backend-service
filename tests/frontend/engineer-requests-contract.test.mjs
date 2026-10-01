import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const web = path.join(root, "src/TFlexDrawingService.Api/wwwroot");
const app = fs.readFileSync(path.join(web, "app.js"), "utf8");
const account = fs.readFileSync(path.join(web, "account.js"), "utf8");
const drawings = fs.readFileSync(path.join(web, "drawings.html"), "utf8");
const accountPage = fs.readFileSync(path.join(web, "account.html"), "utf8");

test("seller submits the current project and unvalidated editor parameters as an engineer request", () => {
  const submit = app.slice(app.indexOf("async function submitEngineerRequest"), app.indexOf("function setJobSubmitDisabled"));
  assert.match(submit, /apiFetch\("\/api\/engineer-requests"/u);
  for (const field of ["projectId", "templateId", "outputFormat"]) {
    assert.match(submit, new RegExp(`${field}:`, "u"));
  }
  assert.match(submit, /\n\s+description,/u);
  assert.match(submit, /parameters: collectParameters\(\)/u);
  assert.doesNotMatch(submit, /getCurrentValidationIssues|setCustomValidity\(.*parameters/u);
  assert.match(submit, /apiFetch\("\/api\/drawings\/classify"/u);
  assert.match(app, /confirmationFingerprint/u);
  assert.match(app, /deviations\.map/u);
  assert.match(drawings, /id="requestEngineerButton"/u);
  assert.match(drawings, /id="engineerRequestDescription"/u);
});

test("account exposes request lifecycle actions, role choices, and unread count", () => {
  for (const role of ["Admin", "Engineer", "Seller"]) assert.match(account, new RegExp(`"${role}"`, "u"));
  for (const action of ["claim", "clarify", "comment", "reject", "cancel", "generate", "issue", "reassign"]) {
    assert.ok(account.includes(`data-request-action="${action}"`), `missing ${action} action`);
  }
  assert.match(account, /\/api\/engineer-requests/u);
  assert.match(account, /\/api\/engineers/u);
  assert.match(account, /item\.status === "InProgress" && item\.canIssue/u);
  assert.match(account, /item\.status === "InProgress" && !item\.canIssue/u);
  assert.match(account, /\/drawings\?engineerRequestId=/u);
  assert.match(account, /renderParameterSummary\(item\.templateId, displayParameters\)/u);
  assert.doesNotMatch(account, /Параметры \(JSON\)|data-request-parameters/u);
  assert.match(accountPage, /id="engineerRequestNewCount"/u);
  assert.match(account, /card\.append\(engineerRequestDetail\)/u);
  assert.doesNotMatch(accountPage, /id="engineerRequestDetail"/u);
  assert.match(account, /function updateEngineerRequestOpenControls\(\)[\s\S]*?for \(const button of engineerRequestsList\.querySelectorAll\('[^']*open-request[^']*'\)\)[\s\S]*?button\.setAttribute\("aria-expanded", String\(expanded\)\)[\s\S]*?button\.textContent = expanded \? localized\("Закрыть", "Close"\)/u);
  assert.match(account, /card\?\.append\(engineerRequestDetail\);\r?\n  updateEngineerRequestOpenControls\(\)/u);
  assert.match(account, /if \(!refresh && state\.activeEngineerRequest\?\.id === id && !engineerRequestDetail\.hidden\)/u);
  assert.match(account, /openEngineerRequest\(item\.id, true\)/u);
  const openRequest = account.slice(account.indexOf("async function openEngineerRequest("), account.indexOf("async function openEngineerRequestFromUrl("));
  assert.match(openRequest, /const openVersion = \+\+engineerRequestOpenVersion;[\s\S]*?await apiFetch[\s\S]*?if \(openVersion !== engineerRequestOpenVersion\) return;[\s\S]*?await sessionRequests\.readJson\(response\);[\s\S]*?if \(openVersion !== engineerRequestOpenVersion\) return;/u);
  assert.match(account, /filter\(item => item\.isUnread\)/u);
  assert.match(account, /card\.classList\.add\("is-unread"\)/u);
  assert.match(account, /engineer-request-unread/u);
  assert.match(fs.readFileSync(path.join(web, "styles.css"), "utf8"), /\.engineer-request-card\.is-unread/u);
});

test("Engineer uses the global project list, sees owners, and cannot delete foreign project assets", () => {
  assert.doesNotMatch(accountPage, /sellerDirectory|sellerProjectsList/u);
  assert.doesNotMatch(account, /sellerDirectory|loadSellerDirectory|loadSellerProjects|canBrowseSellerProjects/u);
  assert.match(account, /roles\?\.includes\("Engineer"\)[\s\S]{0,100}ownerUserName/u);
  assert.match(account, /function canDeleteProjectAssets\(project\)[\s\S]*?if \(canAdmin\(\)\) return true;[\s\S]*?Boolean\(ownerUserName && currentUserName\)[\s\S]*?ownerUserName\.toLocaleLowerCase\(\) === currentUserName\.toLocaleLowerCase\(\)/u);
  assert.match(account, /canDeleteProjectAssets\(project\).*?data-action="delete-project"/u);
  const assetActions = account.slice(account.indexOf("function renderProjectAssetActions("), account.indexOf("function createConfigurationsTable("));
  assert.match(assetActions, /if \(canCreateJobs\(\)\) \{\s*actions\.push\([\s\S]*?data-action="download"[\s\S]*?\}\s*if \(canCreateJobs\(\) && canDeleteProjectAssets\(project\)\) \{\s*actions\.push\([\s\S]*?data-action="delete"/u);
  assert.match(account, /canCreateJobs\(\) && canDeleteProjectAssets\(project\)[\s\S]*?data-action="delete-pricing"/u);
  const app = fs.readFileSync(path.join(web, "app.js"), "utf8");
  assert.match(app, /roles\?\.includes\("Engineer"\)\) && ownerUserName/u);
});

test("only Admin or the assigned Engineer can process a request or review its completed drawing", () => {
  assert.match(account, /const canProcess = isAdmin \|\| roles\.includes\("Engineer"\) && item\.engineerUserName === state\.currentUser\?\.userName/u);
  assert.match(account, /if \(canProcess && \["InProgress", "NeedsClarification"\]\.includes\(item\.status\)\)/u);
  assert.match(account, /if \(canProcess && item\.status === "InProgress" && item\.canIssue\)/u);
  assert.match(account, /item\.reviewFileUrl/u);
  assert.match(account, /reviewUrl\.origin === window\.location\.origin/u);
  assert.match(account, /item\.originalParameters \|\| \{\}/u);
  assert.match(account, /deviationList && \(!isSeller \|\| item\.status === "Ready"\)/u);
  assert.match(account, /const canComment = !\["Ready", "Rejected", "Cancelled"\]\.includes\(item\.status\)/u);
  assert.match(account, /\$\{reviewLink\}\$\{resultLink\}<ul class="engineer-request-comments">\$\{comments\}<\/ul>/u);
  assert.match(account, /\$\{canComment \? '<label class="field">[\s\S]*data-request-message/u);
  assert.match(account, /canComment \? '<button class="secondary" data-request-action="comment"/u);
});

test("engineer opens request in the existing editor and saves only its working copy", () => {
  assert.match(app, /engineerRequestId/u);
  assert.match(app, /request\.engineerUserName\?\.toLowerCase\(\) === state\.currentUser\.userName\?\.toLowerCase\(\)/u);
  assert.match(app, /state\.engineerRequest = \{ \...request/u);
  assert.match(app, /api\/engineer-requests\/\$\{encodeURIComponent\(request\.id\)\}\/parameters/u);
  assert.match(app, /if \(state\.engineerRequest\) return saveEngineerRequestParameters\(\)/u);
  assert.match(app, /if \(state\.engineerRequest\) return;\r?\n  if \(!state\.selectedTemplate\) return;/u);
  assert.match(app, /templateSelect\.disabled = true;/u);
  assert.match(app, /formatSelect\.disabled = true;/u);
  assert.match(app, /\/account\?requestId=/u);
});
