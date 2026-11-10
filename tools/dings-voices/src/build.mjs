// node build.mjs <rawDir>: render every sender x motif at unit scale to float32 mono WAV (finish.py does loudness, ogg and metrics).
import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { SENDERS, MOTIFS, renderClip, VOICES, RHYTHM, NIBU_PROPOSALS } from "./voices.mjs";
import { SR } from "../../earcon-workshop/web/earcon.js";
const out = process.argv[2]; mkdirSync(out, { recursive: true });
const wav32 = (x) => { const b = Buffer.alloc(44 + x.length * 4); b.write("RIFF", 0); b.writeUInt32LE(36 + x.length * 4, 4); b.write("WAVEfmt ", 8); b.writeUInt32LE(16, 16); b.writeUInt16LE(3, 20); b.writeUInt16LE(1, 22); b.writeUInt32LE(SR, 24); b.writeUInt32LE(SR * 4, 28); b.writeUInt16LE(4, 32); b.writeUInt16LE(32, 34); b.write("data", 36); b.writeUInt32LE(x.length * 4, 40); x.forEach((v, i) => b.writeFloatLE(v, 44 + i * 4)); return b; };
const NAMES = [...SENDERS, ...Object.keys(NIBU_PROPOSALS)];
for (const s of NAMES) for (const m of MOTIFS) writeFileSync(join(out, `${s}-${m}.wav`), wav32(renderClip(s, m)));
writeFileSync(join(out, "voices.json"), JSON.stringify({ voices: Object.fromEntries([...SENDERS.map((s) => [s, VOICES[s]]), ...Object.entries(NIBU_PROPOSALS)].map(([s, v]) => [s, { object: v.object, family: v.family, caption: v.caption }])), rhythm: RHYTHM }, null, 1));
console.log(`wrote ${NAMES.length * MOTIFS.length} raw clips to ${out}`);
