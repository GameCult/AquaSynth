#!/usr/bin/env node
// Builds one self-contained listening page. Usage:
//   node build.mjs --manifest kit.json --clips DIR --out DIR   (writes DIR/listen.html; --clips defaults to the manifest's folder)
import { readFileSync, writeFileSync, mkdirSync, existsSync, statSync } from "node:fs";
import { createHash } from "node:crypto";
import { createRequire } from "node:module";
import { dirname, extname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const L = createRequire(import.meta.url)("./logic.cjs");
const MIME = { ".wav": "audio/wav", ".mp3": "audio/mpeg", ".ogg": "audio/ogg", ".flac": "audio/flac" };

// Throws one Error listing every manifest problem and every missing file. Returns {html, hash, bytes}.
export function buildKit(manifest, clipsDir) {
  const errs = L.validateManifest(manifest);
  if (errs.length === 0) for (const c of manifest.clips) {
    const f = resolve(clipsDir, c.file);
    if (!f.startsWith(resolve(clipsDir) + "/") && !f.startsWith(resolve(clipsDir) + "\\")) errs.push(`clip ${c.id}: ${c.file} leaves the clips folder`);
    else if (!existsSync(f) || !statSync(f).isFile()) errs.push(`clip ${c.id}: file not found: ${c.file}`);
  }
  if (errs.length) throw new Error("manifest invalid:\n  " + errs.join("\n  "));
  const audio = {}, h = createHash("sha256");
  h.update(JSON.stringify(manifest));
  for (const c of manifest.clips) {
    const buf = readFileSync(resolve(clipsDir, c.file));
    h.update(buf);
    audio[c.id] = { mime: MIME[extname(c.file).toLowerCase()], b64: buf.toString("base64") };
  }
  const hash = h.digest("hex").slice(0, 8);
  const data = JSON.stringify({ manifest, hash, audio }).replace(/</g, "\\u003c");
  const logic = `globalThis.AquaListen=(()=>{const module={exports:{}};${readFileSync(join(here, "logic.cjs"), "utf8")}\nreturn module.exports})();`;
  const part = (n) => readFileSync(join(here, "web", n), "utf8");
  const esc = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;");
  // function replacers: no `$` patterns in the substituted text are interpreted
  const html = part("index.html")
    .replace("/*TITLE*/", () => esc(manifest.kit.title)).replace("/*STYLES*/", () => part("styles.css"))
    .replace("/*DATA*/", () => data).replace("/*LOGIC*/", () => logic.replace(/<\/script/gi, "<\\/script")).replace("/*PAGE*/", () => part("page.js").replace(/<\/script/gi, "<\\/script"));
  return { html, hash, bytes: Buffer.byteLength(html) };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const a = process.argv.slice(2), opt = (n) => { const i = a.indexOf(n); return i < 0 ? undefined : a[i + 1]; };
  const mf = opt("--manifest"), out = opt("--out");
  if (!mf || !out) { console.error("usage: node build.mjs --manifest kit.json --clips DIR --out DIR"); process.exit(2); }
  try {
    const manifest = JSON.parse(readFileSync(mf, "utf8"));
    const r = buildKit(manifest, resolve(opt("--clips") ?? dirname(resolve(mf))));
    mkdirSync(out, { recursive: true });
    writeFileSync(join(out, "listen.html"), r.html);
    console.log(`built ${join(out, "listen.html")}\nclips ${manifest.clips.length}  bytes ${r.bytes}  hash ${r.hash}`);
    if (r.bytes > 100e6) console.log("warning: over 100 MB; some browsers and mail systems will struggle. Trim clips.");
  } catch (e) { console.error(e.message); process.exit(1); }
}
