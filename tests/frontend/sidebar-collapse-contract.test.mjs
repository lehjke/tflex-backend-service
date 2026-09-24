import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const webRoot = path.join(root, "src/TFlexDrawingService.Api/wwwroot");
const html = name => fs.readFileSync(path.join(webRoot, `${name}.html`), "utf8");

test("desktop sidebar toggle and navigation icons are shared across pages", () => {
  const pages = ["index", "drawings", "pricing", "account"].map(html);
  for (const page of pages) {
    assert.match(page, /class="sidebar__menu-toggle"[^>]*aria-controls="sidebarMenu"[^>]*aria-expanded="false"/u);
    assert.ok(page.indexOf("/sidebar-init.js") < page.indexOf("/styles.css"));
    assert.match(page, /<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M15 5 8 12l7 7"\/>/u);
    assert.equal((page.match(/class="sidebar__nav-icon"/gu) || []).length, 3);
    assert.match(page, /<rect x="5\.5" y="3\.5" width="13" height="17" rx="2"\/>/u);
    assert.match(page, /class="sidebar__help-icon"/u);
    assert.match(page, /styles\.css\?v=20260924-sidebar-collapsed-spacing-1/u);
    assert.match(page, /shell\.js\?v=20260924-sidebar-collapse-2/u);
  }
});

test("sidebar collapse persists safely and keeps mobile menu state separate", () => {
  const shell = fs.readFileSync(path.join(webRoot, "shell.js"), "utf8");
  const styles = fs.readFileSync(path.join(webRoot, "styles.css"), "utf8");

  const init = fs.readFileSync(path.join(webRoot, "sidebar-init.js"), "utf8");
  assert.match(init, /localStorage\.getItem\("tflex-sidebar-collapsed"\)[\s\S]*?classList.add\("sidebar-collapsed"\)/u);
  assert.match(shell, /localStorage\.getItem\(sidebarStorageKey\)[\s\S]*?catch/u);
  assert.match(shell, /localStorage\.setItem\(sidebarStorageKey, String\(collapsed\)\)[\s\S]*?catch/u);
  assert.match(shell, /removeAttribute\("aria-expanded"\)/u);
  assert.match(shell, /setAttribute\("aria-pressed", collapsed \? "true" : "false"\)/u);
  assert.match(shell, /removeAttribute\("aria-pressed"\)/u);
  assert.match(shell, /if \(mobileMenuQuery\.matches\) \{\s*toggleMobileMenu\(\);\s*\} else \{/u);
  assert.match(styles, /body\.sidebar-ready \.app-shell \{\s*transition: grid-template-columns 180ms ease;/u);
  assert.match(styles, /@media \(prefers-reduced-motion: reduce\)/u);
  assert.match(styles, /:is\(body\.sidebar-collapsed, html\.sidebar-collapsed\) \.sidebar__language \{\s*display: none;/u);
  assert.match(styles, /@media \(min-width: 901px\) \{[\s\S]*?:is\(body\.sidebar-collapsed, html\.sidebar-collapsed\) \.app-shell\s*\{\s*grid-template-columns: 72px/u);
  assert.match(styles, /@media \(max-width: 900px\) \{[\s\S]*?\.sidebar \{\s*position: sticky/u);
  assert.match(styles, /\.sidebar__nav a \{[\s\S]*?border: 1px solid rgb\(255 255 255 \/ 16%\);/u);
  assert.doesNotMatch(styles, /@media \(max-width: 900px\) \{[\s\S]*?\.sidebar__nav a \{[^}]*border: 1px solid transparent;/u);
  assert.match(styles, /:is\(body\.sidebar-collapsed, html\.sidebar-collapsed\) \.brand \{[\s\S]*?mlt-mark-favicon\.svg/u);
  assert.match(styles, /:is\(body\.sidebar-collapsed, html\.sidebar-collapsed\) \.sidebar__help \{[\s\S]*?width: 50px;[\s\S]*?background: transparent;/u);
  assert.match(styles, /:is\(body\.sidebar-collapsed, html\.sidebar-collapsed\) \.sidebar__help-button \{\s*width: 50px;/u);
});
