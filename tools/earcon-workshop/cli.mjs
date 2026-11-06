// node cli.mjs <identity> <outDir> [key=value ...]   e.g. node cli.mjs warm-bell ./earcons tempo=1.2 level=0.6
// Writes the WAVs, pack.json and README.md the page's zip contains, from the same renderer.
import { mkdir, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { IDENTITIES, renderFamily, packFiles } from "./web/earcon.js";

const [identity, outDir, ...kv] = process.argv.slice(2);
if (!IDENTITIES[identity] || !outDir) { console.error(`usage: node cli.mjs <${Object.keys(IDENTITIES).join("|")}> <outDir> [tempo= transpose= brightness= decay= level=]`); process.exit(2); }
const params = Object.fromEntries(kv.map((s) => s.split("=")).map(([k, v]) => [k, Number(v)]));
await mkdir(outDir, { recursive: true });
for (const f of packFiles(renderFamily(identity, undefined, params))) await writeFile(join(outDir, f.name), f.bytes);
console.log(`wrote ${outDir}`);
