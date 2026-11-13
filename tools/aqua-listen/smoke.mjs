// Browser smoke for a built page: node smoke.mjs listen.html  (needs playwright-core and a Chromium; set PLAYWRIGHT_CORE=/path/to/playwright-core
// and optionally CHROME=/path/to/chrome). Not part of `node test.mjs`: the repo has no browser dependency.
import { createRequire } from "node:module";
import { readFileSync, writeFileSync, mkdtempSync } from "node:fs";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { buildKit } from "./build.mjs";
import { resolve, join } from "node:path";
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
// http-Android shape: no async clipboard API at all, so only the execCommand path can copy
await page.evaluate(() => { Object.defineProperty(navigator, "clipboard", { value: undefined, configurable: true }); window.__copied = null; document.execCommand = (c) => { window.__copied = document.activeElement.value; return c === "copy"; }; });
await page.click("#copy-json");
check((await page.textContent("#export-msg")).startsWith("Copied") && (await page.evaluate(() => window.__copied)) === text, "without the clipboard API, the execCommand fallback copies the full JSON");
await page.reload();
await page.click("#export");
check((await page.inputValue("#export-text")).includes('"verdict": "fix"'), "reload loses nothing: export after reload still has the answers");
check(requests.length === 0, `no network requests (${requests.join(", ")})`);
check(errors.length === 0, `no page errors (${errors.join("; ")})`);
// ---- Autosave persistence across a rebuild. Served over http at one fixed path so the origin (and localStorage) is the same for
// the page as given (A) and a rebuild of the same kit with a different content hash (B: a note edit and one byte appended to a clip).
const html = readFileSync(file, "utf8"), kit = JSON.parse(html.match(/<script id="kit-data"[^>]*>([\s\S]*?)<\/script>/)[1].replace(/\\u003c/g, "<"));
const tmp = mkdtempSync(join(tmpdir(), "aqua-listen-smoke-"));
for (const c of kit.manifest.clips) { const buf = Buffer.from(kit.audio[c.id].b64, "base64"); writeFileSync(join(tmp, c.file), c.id === kit.manifest.clips[0].id ? Buffer.concat([buf, Buffer.from([0])]) : buf); }
const B = buildKit({ ...kit.manifest, kit: { ...kit.manifest.kit, note: (kit.manifest.kit.note || "") + " (rebuilt)" } }, tmp);
check(B.hash !== kit.hash, `the rebuilt page has a different content hash (${kit.hash} -> ${B.hash})`);
let serving = html;
const srv = createServer((q, r) => { r.setHeader("content-type", "text/html"); r.end(q.url.startsWith("/p/listen.html") ? serving : "x"); });
await new Promise((ok) => srv.listen(0, "127.0.0.1", ok));
const url = `http://127.0.0.1:${srv.address().port}/p/listen.html`;
const ids = kit.manifest.clips.map((c) => c.id), mfst = kit.manifest.kit.id;
const ctx2 = await browser.newContext(), pg = await ctx2.newPage(), errs2 = [];
pg.on("pageerror", (e) => errs2.push(e.message));
const begin = async () => { await pg.goto(url); if (await pg.locator("#consent").count()) { await pg.check("#consent"); await pg.click("button:has-text('Start listening')"); } };
const firstId = async () => (await pg.locator("section.card").first().getAttribute("id")).slice(5);
await begin();
const id1 = await firstId(), card = () => pg.locator("#clip-" + id1);
await card().locator("input[value=drop]").check(); await card().locator("input[type=text]").fill("rebuild survivor");
await pg.reload();
check(await card().locator("input[value=drop]").isChecked(), "persistence: answer survives a reload of the same page");
serving = B.html; await pg.reload();
check((await pg.locator("body").innerText()).includes("(rebuilt)"), "persistence: the rebuilt page is what loaded");
check(await card().locator("input[value=drop]").isChecked() && (await card().locator("input[type=text]").inputValue()) === "rebuild survivor", "persistence: answers survive a rebuild with a different content hash");
const keys = await pg.evaluate(() => Object.keys(localStorage));
check(keys.length === 1 && keys[0].startsWith("aqua-listen.v2:"), `persistence: one save, under the hash-free key (${keys})`);
// an earlier build's hash-keyed save is adopted when the new key is empty, and never overwrites a new-key save
await pg.evaluate(() => localStorage.clear());
const old = (seed, verdict) => JSON.stringify({ seed, answers: { [ids[ids.length - 1]]: { verdict, landed: 2, reason: "typed before the rebuild" } }, pairs: {}, device: "speakers", notes: "", listener: "Meta", consent: true });
const oldKey = `aqua-listen:${mfst}:${kit.hash}`;
await pg.evaluate(([k, v, k2, v2]) => { localStorage.setItem(k, v); localStorage.setItem(k2, v2); }, [oldKey, old(11, "keep"), `aqua-listen:other-kit:${kit.hash}`, old(12, "drop")]);
await pg.reload();
await pg.click("button:has-text('Start listening')").catch(() => {});
const lastCard = pg.locator("#clip-" + ids[ids.length - 1]);
check(await lastCard.locator("input[value=keep]").isChecked() && (await lastCard.locator("input[type=text]").inputValue()) === "typed before the rebuild", "migration: the hash-keyed save from the earlier build is adopted");
check((await pg.evaluate(() => Object.keys(localStorage))).some((k) => k.startsWith("aqua-listen.v2:")) && (await pg.evaluate(([k]) => JSON.parse(localStorage.getItem(Object.keys(localStorage).find((x) => x.startsWith("aqua-listen.v2:")))).seed, [0])) === 11, "migration: saved under the new key, with the old seed");
check(await pg.evaluate((k) => localStorage.getItem(k) !== null, `aqua-listen:other-kit:${kit.hash}`), "migration: another kit's entry is left untouched");
await pg.evaluate(([k, v]) => { localStorage.setItem(k, v); localStorage.setItem(Object.keys(localStorage).find((x) => x.startsWith("aqua-listen.v2:")), v); }, [oldKey, old(13, "fix")]);
await pg.reload();
check(await pg.locator("#clip-" + ids[ids.length - 1]).locator("input[value=fix]").isChecked(), "migration: an existing new-key save is used, not overwritten by the hash-keyed one");
// a save with no pairs and array answers must not crash the page; a reordered kit keeps answers and drops pair picks
await pg.evaluate(([k]) => { localStorage.clear(); localStorage.setItem(k, JSON.stringify({ seed: 5, answers: [], consent: true })); }, [(await pg.evaluate(() => 0), `aqua-listen.v2:${mfst}:${[...ids].sort().join(",")}`)]);
await pg.reload();
check(await pg.locator("audio").count() === ids.length && errs2.length === 0, "a save without pairs and with array answers loads without errors");
await pg.evaluate(([k, v]) => localStorage.setItem(k, v), [oldKey, old(14, "drop")]);
pg.once("dialog", (d) => d.accept()); await pg.click("text=Erase this kit's saved answers"); await pg.waitForLoadState();
check(await pg.evaluate(() => Object.keys(localStorage).filter((k) => k.startsWith("aqua-listen:") && !k.includes("other-kit") || k.startsWith("aqua-listen.v2:")).length) === 0 && await pg.locator("#consent").count() === 1, "erase removes the new-key save and the adoptable hash-keyed one (nothing resurrects)");
check(errs2.length === 0, `persistence: no page errors (${errs2.join("; ")})`);
srv.close();
await browser.close();
