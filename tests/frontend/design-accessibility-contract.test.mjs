import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const webRoot = path.join(repositoryRoot, "src/TFlexDrawingService.Api/wwwroot");

function readWebSource(fileName) {
  return fs.readFileSync(path.join(webRoot, fileName), "utf8");
}

test("home cards keep link navigation and restore interruptible disclosure motion", () => {
  const html = readWebSource("index.html");
  const source = readWebSource("home.js");
  const styles = readWebSource("styles.css");

  assert.match(html, /<article class="home-template-card"/u);
  assert.match(html, /<a class="home-template-card__button" href="\/drawings"/u);
  assert.match(html, /<a class="home-template-card__button" href="\/pricing"/u);
  assert.doesNotMatch(source, /addEventListener\("pointerdown"/u);
  assert.match(source, /function setTemplateCardState\(card, isOpen\)/u);
  assert.match(source, /function openTemplateCard\(card\)/u);
  assert.match(source, /function scheduleTemplateCardClose\(card\)/u);
  assert.match(source, /addEventListener\("pointerenter"/u);
  assert.match(source, /addEventListener\("pointerleave"/u);
  assert.match(source, /addEventListener\("focusin"/u);
  assert.match(source, /addEventListener\("focusout"/u);
  assert.match(source, /setupTemplateCards\(\);/u);
  assert.match(styles, /\.home-template-card \{[\s\S]*?height: 240px;[\s\S]*?transition: height 0\.34s/u);
  assert.match(styles, /\.home-template-card\.is-open \{\s*height: 459px;/u);
  assert.match(styles, /\.home-template-card__description \{[\s\S]*?opacity: 0;[\s\S]*?transition: opacity/u);
  assert.match(styles, /\.home-template-card\.is-open \.home-template-card__description \{\s*opacity: 1;/u);
  assert.match(styles, /@media \(max-width: 620px\) \{[\s\S]*?\.home-template-card\.is-open \{\s*height: 500px;/u);
  assert.match(styles, /\.home-template-card\.is-open \.home-template-card__footer \{[\s\S]*?flex-direction: column;/u);
});

test("pricing uses progressive disclosure and a lazy keyboard-operated visual listbox", () => {
  const html = readWebSource("pricing.html");
  const source = readWebSource("pricing.js");
  const disclosures = html.match(/<details class="pricing-section/gu) || [];
  const initiallyOpen = html.match(/<details class="pricing-section[^>]* open>/gu) || [];

  assert.equal(disclosures.length, 12);
  assert.equal(initiallyOpen.length, 12);
  assert.match(source, /select\.tabIndex = -1/u);
  assert.match(source, /select\.setAttribute\("aria-hidden", "true"\)/u);
  assert.match(source, /menu\.replaceChildren\(\)/u);
  for (const key of ["ArrowDown", "ArrowUp", "Home", "End", "Escape", "Enter"]) {
    assert.ok(source.includes(`event.key === "${key}"`), `visual select is missing ${key}`);
  }
  assert.match(source, /handleVisualSelectTypeahead/u);
  assert.match(source, /openParentDisclosures\(target\)/u);
  assert.match(source, /function normalizeVisualSelectField/u);
  assert.match(source, /if \(field\.tagName === "LABEL"\)/u);
  assert.match(source, /label\.htmlFor = triggerId/u);
  assert.match(source, /button\.setAttribute\("aria-labelledby"/u);
  assert.match(source, /button\.setAttribute\("aria-describedby"/u);
  assert.match(html, /class="pricing-mobile-summary" aria-hidden="true"/u);
  assert.match(html, /id="pricingWarnings"[^>]*role="status"[^>]*aria-live="polite"/u);
  assert.match(source, /pricingWarnings\.setAttribute\("role", isError \? "alert" : "status"\)/u);
});

test("editor exposes labels, preserves text selection, announces collisions, and can retry boot", () => {
  const html = readWebSource("drawings.html");
  const source = readWebSource("app.js");

  assert.match(source, /showAllParameters: true/u);
  assert.match(html, /id="showAllParametersToggle"[^>]*checked/u);
  assert.match(html, /\/app\.js\?v=20260807-show-all-1/u);
  assert.match(source, /document\.createElement\("label"\)/u);
  assert.match(source, /input\.setSelectionRange\(/u);
  assert.match(source, /compositionstart/u);
  assert.match(source, /createDisplayInput[\s\S]*?setAttribute\([\s\S]*?"aria-label"/u);
  assert.match(html, /id="editorHeading" tabindex="-1"/u);
  assert.match(html, /id="validationPanel"[^>]*role="status"[^>]*aria-live="polite"/u);
  assert.match(source, /function updateValidationPanel\(issues = \[\], \{ announceErrors = false \} = \{\}\)/u);
  assert.match(source, /updateValidationPanel\(validationIssues, \{ announceErrors: true \}\)/u);
  assert.match(html, /id="shaftCollisionStatus"[^>]*role="alert"/u);
  assert.match(source, /\.shaft-preview-svg__door--collision/u);
  assert.match(source, /previewImage\?\.setAttribute\("aria-describedby", "shaftCollisionStatus"\)/u);
  assert.match(source, /shaftPreviewContent\.hidden = false;\s*updatePreviewCollisionStatus\(\)/u);
  assert.match(html, /id="pageLoadError"[^>]*role="alert"/u);
  assert.match(html, /id="pageLoadErrorTitle"/u);
  assert.match(html, /id="pageLoadErrorMessage"/u);
  assert.match(source, /retryPageLoadButton\?\.addEventListener\("click"/u);
  assert.match(source, /loadCurrentUser\(\{ required: true \}\)/u);
  assert.match(source, /loadTemplates\(\{ required: true \}\)/u);
  assert.match(source, /loadProjects\(null, \{ required: true \}\)/u);
  assert.match(source, /refreshJobs\(\{ required: true \}\)/u);
  assert.match(source, /async function boot\(\{ focusOnSuccess = false, focusOnError = false, context = "load" \} = \{\}\)/u);
  assert.match(source, /statusPanel\.innerHTML = `<div class="status pending">Pending<\/div>`;\s*statusPanel\.setAttribute\("aria-busy", "false"\)/u);
  assert.match(source, /if \(state\.pollRequestToken !== null\) \{\s*scheduleJobPoll\(jobId\);\s*return;/u);
  assert.match(source, /const requestToken = \{\};\s*state\.pollRequestToken = requestToken/u);
  assert.match(source, /if \(state\.pollRequestToken === requestToken\) \{\s*state\.pollRequestToken = null;/u);
  assert.doesNotMatch(source, /setInterval\(/u);
  assert.match(source, /function scheduleJobPoll\(jobId, delay = 1200\)/u);
  assert.match(source, /function handleJobPollingFailure\(jobId\)/u);
  assert.match(source, /state\.pollErrorAnnounced/u);
  assert.match(source, /Math\.min\(1200 \* \(2 \*\* Math\.min\(state\.pollFailureCount, 4\)\), 15000\)/u);
  assert.match(source, /state\.lastRenderedJobFingerprint === fingerprint/u);
  assert.match(source, /renderJob\(state\.latestJob, \{ force: true \}\)/u);
  assert.match(source, /statusPanel\.setAttribute\("role", "alert"\);\s*statusPanel\.setAttribute\("aria-live", "assertive"\)/u);
  assert.match(source, /async function logout\(\)[\s\S]*?showPageLoadFailure\(\{ focus: true, context: "logout" \}\)/u);
  assert.match(source, /function syncPageLoadErrorCopy\(context = pageLoadErrorContext\)/u);
  assert.match(source, /const context = pageLoadErrorContext;\s*sessionRequests\.invalidate\(\);\s*void boot\(\{ focusOnSuccess: true, focusOnError: true, context \}\)/u);
  assert.match(source, /const authenticated = await loadCurrentUser\(\{ required: true \}\);\s*errorContext = "load"/u);
  assert.match(source, /showPageLoadFailure\(\{ focus: focusOnError, context: errorContext \}\)/u);
  assert.doesNotMatch(html, /Queue 0\/50/u);
});

test("account protects destructive and duplicate generation actions and recovers from load errors", () => {
  const html = readWebSource("account.html");
  const source = readWebSource("account.js");

  assert.match(source, /async function deleteConfiguration[\s\S]*?if \(!confirm\(confirmation\)\)/u);
  assert.match(source, /activeGenerationActions: new Map\(\)/u);
  assert.match(source, /state\.activeGenerationActions\.has\(generationKey\)/u);
  assert.match(source, /finally \{[\s\S]*?state\.activeGenerationActions\.delete\(generationKey\)/u);
  assert.match(html, /id="pageLoadError"[^>]*role="alert"/u);
  assert.match(html, /id="pageLoadErrorTitle"/u);
  assert.match(html, /id="pageLoadErrorMessage"/u);
  assert.match(source, /function boot\(\{ context = "load" \} = \{\}\)[\s\S]*?if \(bootPromise\) return bootPromise/u);
  assert.match(source, /requireSuccessfulLoadResponse\(\s*configurationsResponse/u);
  assert.doesNotMatch(source, /if \(!configurationsResponse\.ok\) return \[project\.id, \[\]\]/u);
  assert.match(source, /activeAdminUserActions: new Set\(\)/u);
  assert.match(source, /row\.querySelectorAll\("button, input"\)/u);
  assert.match(source, /state\.activeAdminUserActions\.has\(userName\)/u);
  assert.match(source, /async function logout\(\)[\s\S]*?showPageLoadError\(\{ context: "logout" \}\)/u);
  assert.match(source, /function updatePageLoadErrorCopy\(context = pageLoadErrorContext\)/u);
  assert.match(source, /const context = pageLoadErrorContext;\s*void boot\(\{ context \}\)/u);
  assert.match(source, /const authenticated = await loadCurrentUser\(\);\s*errorContext = "load"/u);
  assert.match(source, /showPageLoadError\(\{ context: errorContext \}\)/u);
  assert.match(source, /response\.status === 401 \|\| response\.status === 403/u);
});

test("responsive and user-preference contracts cover the audited breakpoints", () => {
  const source = readWebSource("styles.css");
  const shell = readWebSource("shell.js");

  assert.match(source, /@media \(max-width: 1280px\)[\s\S]*?\.pricing-grid \{\s*grid-template-columns: minmax\(0, 1fr\)/u);
  assert.match(source, /@media \(max-width: 1200px\)[\s\S]*?\.account-columns \{\s*grid-template-columns: minmax\(0, 1fr\)/u);
  assert.match(source, /\.pricing-mobile-summary \{[\s\S]*?top: 96px/u);
  assert.match(source, /\.pricing-section > summary\.panel__header \{\s*flex-direction: row/u);
  assert.match(source, /\.pricing-section \{[\s\S]*?background: #ffffff;/u);
  assert.match(source, /\.pricing-result \{[\s\S]*?border-radius: var\(--radius\);/u);
  assert.doesNotMatch(source, /\.smec-excel-section \{[\s\S]*?background:/u);
  assert.match(source, /\.shaft-preview-svg__door--collision \{[\s\S]*?stroke: var\(--bad\)/u);
  assert.match(source, /@media \(prefers-contrast: more\)/u);
  assert.match(source, /@media \(forced-colors: active\)/u);
  assert.match(source, /@media \(prefers-reduced-transparency: reduce\)/u);
  assert.match(source, /\.intro p\.role-note:not\(\[hidden\]\)/u);
  assert.match(source, /input:focus,[\s\S]*?box-shadow: none;/u);
  assert.match(source, /#parametersForm input:not\(:disabled\),[\s\S]*?cursor: pointer;/u);
  assert.doesNotMatch(source, /outline: 3px solid var\(--focus\)/u);
  assert.doesNotMatch(source, /box-shadow: 0 0 0 3px rgb\(24 47 96/u);
  assert.match(shell, /workspace\.inert = true/u);
  assert.match(shell, /workspace\.setAttribute\("aria-hidden", "true"\)/u);
  assert.match(shell, /const preserveSearchFocus = Boolean/u);
  assert.match(shell, /preserveSearchFocus && document\.body\.classList\.contains\("mobile-menu-open"\)/u);
});

test("all frontend modules share one i18n instance", () => {
  const moduleNames = ["home.js", "app.js", "account.js", "pricing.js", "shell.js", "file-preview.js"];
  const imports = moduleNames.flatMap(fileName =>
    [...readWebSource(fileName).matchAll(/\.\/i18n\.js\?v=([^"']+)/gu)]
      .map(match => ({ fileName, version: match[1] })));

  assert.equal(imports.length, moduleNames.length);
  assert.deepEqual([...new Set(imports.map(item => item.version))], ["20260806-design-fixes-1"]);

  for (const fileName of ["app.js", "account.js"]) {
    assert.match(
      readWebSource(fileName),
      /\.\/file-preview\.js\?v=20260806-design-fixes-1/u,
      `${fileName} must invalidate the file-preview module graph`);
  }

  const i18n = readWebSource("i18n.js");
  assert.match(i18n, /\["Поиск: Быстрый доступ", "Search: Quick access"\]/u);
  assert.match(i18n, /const lobbyLabel = value\.match\(\/\^Лобби/u);
});

test("all frontend pages share the current stylesheet cache key", () => {
  const pageNames = ["index.html", "drawings.html", "pricing.html", "account.html"];
  for (const pageName of pageNames) {
    assert.match(
      readWebSource(pageName),
      /\/styles\.css\?v=20260824-home-card-motion-1/u,
      `${pageName} must load the current parameter-control stylesheet`);
  }
});

test("all pages resolve authentication behind a pixel-matched loading shell", () => {
  const variants = new Map([
    ["index.html", "home"],
    ["drawings.html", "editor"],
    ["pricing.html", "pricing"],
    ["account.html", "account"]
  ]);

  for (const [fileName, variant] of variants) {
    const html = readWebSource(fileName);
    assert.match(
      html,
      new RegExp(`id="pageSkeleton" class="page-skeleton page-skeleton--${variant}"[^>]*aria-busy="true"`, "u"),
      `${fileName} must start in its matching loading shell`);
    assert.match(html, /<main id="guestMain"[^>]* hidden>/u, `${fileName} must not flash the access form`);
    assert.ok(
      html.indexOf('id="pageSkeleton"') < html.indexOf('id="guestMain"'),
      `${fileName} must render loading before auth-dependent content`);
  }

  for (const fileName of ["home.js", "pricing.js"]) {
    const source = readWebSource(fileName);
    assert.match(source, /function hidePageSkeleton\(\)/u);
    assert.match(source, /finally \{\s*hidePageSkeleton\(\);\s*\}/u);
  }
});

test("profile links reference existing account, user, and role labels", () => {
  for (const fileName of ["index.html", "drawings.html", "pricing.html", "account.html"]) {
    const html = readWebSource(fileName);
    const match = html.match(/class="user-panel__account-link"[^>]*aria-labelledby="([^"]+)"/u);
    assert.ok(match, `${fileName} is missing the profile accessible-name references`);
    for (const id of match[1].split(/\s+/u)) {
      assert.match(html, new RegExp(`id="${id}"`, "u"), `${fileName} is missing #${id}`);
    }
  }
});
