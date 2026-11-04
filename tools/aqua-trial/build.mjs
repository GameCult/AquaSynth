// Assembles dist/ (the static trial site): web/, logic.mjs, the trial patch, and the faustwasm runtime copied from
// the sibling aqua-playground's node_modules (run `npm install` there first). Prints a content hash for the
// publish path: minds.gamecult.org/aqua/trial-<hash>/. Usage: node build.mjs [outDir]
import { cpSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync } from "node:fs";
import { createHash } from "node:crypto";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const out = resolve(process.argv[2] ?? join(here, "dist"));
const play = join(here, "../aqua-playground");
const fw = join(play, "node_modules/@grame/faustwasm");
rmSync(out, { recursive: true, force: true });
mkdirSync(join(out, "vendor/faustwasm"), { recursive: true });
cpSync(join(here, "web"), out, { recursive: true });
cpSync(join(here, "logic.mjs"), join(out, "logic.mjs"));
cpSync(join(here, "thruster-trial.dsp"), join(out, "thruster-trial.dsp"));
cpSync(join(play, "web/ui.js"), join(out, "ui.js"));
cpSync(join(fw, "dist/esm/index.js"), join(out, "vendor/faustwasm/index.js"));
cpSync(join(fw, "COPYING.txt"), join(out, "vendor/faustwasm/COPYING.txt"));
for (const f of ["libfaust-wasm.js", "libfaust-wasm.wasm", "libfaust-wasm.data"])
  cpSync(join(fw, "libfaust-wasm", f), join(out, "vendor/libfaust-wasm", f));

const files = (d) => readdirSync(d).sort().flatMap((n) => (statSync(join(d, n)).isDirectory() ? files(join(d, n)) : [join(d, n)]));
const h = createHash("sha256");
for (const f of files(out)) { h.update(relative(out, f)); h.update(readFileSync(f)); }
console.log(`built ${out}\nhash ${h.digest("hex").slice(0, 7)}`);
