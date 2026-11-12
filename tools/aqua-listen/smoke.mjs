// Browser smoke for a built page: node smoke.mjs listen.html  (needs playwright-core and a Chromium; set PLAYWRIGHT_CORE=/path/to/playwright-core
// and optionally CHROME=/path/to/chrome). Not part of `node test.mjs`: the repo has no browser dependency.
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_CORE ?? "playwright-core");
const file = resolve(process.argv[2]);
const browser = await chromium.launch({ executablePath: process.env.CHROME || undefined });
// MOBILE=1 drives an Android-Chrome-shaped context (touch, small viewport, mobile UA) with no downloads accepted.
const mobile = !!process.env.MOBILE;
const ctx = await browser.newContext(mobile
  ? { acceptDownloads: false, viewport: { width: 412, height: 915 }, hasTouch: true, isMobile: true, deviceScaleFactor: 2.6, permissions: ["clipboard-read", "clipboard-write"],
      userAgent: "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36" }
  : { acceptDownloads: true, permissions: ["clipboard-read", "clipboard-write"] }), page = await ctx.newPage();
const requests = [], errors = [];
page.on("request", (r) => { if (!r.url().startsWith("file:") && !r.url().startsWith("blob:") && !r.url().startsWith("data:")) requests.push(r.url()); });
page.on("pageerror", (e) => errors.push(e.message));
await page.goto("file://" + file);
const check = (c, m) => { if (!c) { console.error("FAIL", m); process.exitCode = 1; } else console.log("ok", m); };
check(await page.locator("button:has-text('Start listening')").isDisabled(), "Start is disabled until consent is ticked");
check((await page.locator("audio").count()) === 0, "no clip is shown before consent");
await page.check("#consent"); await page.fill("#alias", "Tester"); await page.click("button:has-text('Start listening')");
const total = await page.locator("audio").count();
check(total > 0, `${total} clips shown after consent`);
check(!/Title|title/.test(await page.locator("main").innerText()), "titles are hidden while listening");
await page.check("input[name=device][value=headphones]");
const first = page.locator("section.card").first();
await first.locator("input[value=fix]").check(); await first.locator("input[type=radio][name^=l-][value='4']").check(); await first.locator("input[type=text]").fill("a bit thin");
await page.reload();
check(await page.locator("section.card").first().locator("input[value=fix]").isChecked(), "answers survive a reload (localStorage autosave)");
check((await page.locator("section.card").first().locator("input[type=text]").inputValue()) === "a bit thin", "reason survives a reload");
await page.click("#export");
const text = await page.inputValue("#export-text");
check(await page.locator("#export-text").evaluate((e) => e.readOnly), "export textarea is read-only");
const v = JSON.parse(text);
check(v.listener === "Tester" && v.device === "headphones" && v.clips[0].verdict === "fix" && v.clips[0].landed === 4 && v.clips[0].device === "headphones", "textarea carries the verdict JSON");
check(v.consent === true && v.clips.length === total, "consent recorded, every clip present");
await page.click("#copy-json");
check((await page.evaluate(() => navigator.clipboard.readText())) === text, "Copy button puts the full JSON on the clipboard");
await page.evaluate(() => { navigator.clipboard.writeText = () => Promise.reject(new Error("denied")); });
await page.click("#copy-json");
check(/Copied|blocked/.test(await page.textContent("#export-msg")), "copy falls back to execCommand without throwing");
await page.reload();
await page.click("#export");
check((await page.inputValue("#export-text")).includes('"verdict": "fix"'), "reload loses nothing: export after reload still has the answers");
check(requests.length === 0, `no network requests (${requests.join(", ")})`);
check(errors.length === 0, `no page errors (${errors.join("; ")})`);
await browser.close();
