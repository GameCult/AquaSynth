// Assembles dist/ (the static site): web/, lowered/ patches + catalog.json, and the faustwasm runtime
// (ESM wrapper + libfaust wasm) copied from node_modules. Usage: node build.mjs [outDir]
import { cpSync, mkdirSync, rmSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const out = resolve(process.argv[2] ?? join(here, "dist"));
const fw = join(here, "node_modules/@grame/faustwasm");
rmSync(out, { recursive: true, force: true });
mkdirSync(join(out, "vendor/faustwasm"), { recursive: true });
cpSync(join(here, "web"), out, { recursive: true });
cpSync(join(here, "lowered"), out, { recursive: true });
cpSync(join(fw, "dist/esm/index.js"), join(out, "vendor/faustwasm/index.js"));
cpSync(join(fw, "COPYING.txt"), join(out, "vendor/faustwasm/COPYING.txt"));
for (const f of ["libfaust-wasm.js", "libfaust-wasm.wasm", "libfaust-wasm.data"])
  cpSync(join(fw, "libfaust-wasm", f), join(out, "vendor/libfaust-wasm", f));
console.log(`built ${out}`);
