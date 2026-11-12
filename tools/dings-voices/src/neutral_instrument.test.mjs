// node neutral_instrument.test.mjs: the instrument id build.mjs renders for the neutral fallback is a DingCatalog id.
// The catalog's checked-in mirror is tools/aqua-feel/lowered-dings/*.dsp (AquaLower generates it; a dotnet test guards it).
import assert from "node:assert/strict";
import { readdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { NEUTRAL_INSTRUMENT_ID } from "./voices.mjs";
const dir = fileURLToPath(new URL("../../aqua-feel/lowered-dings/", import.meta.url));
const ids = readdirSync(dir).filter((f) => f.endsWith(".dsp")).map((f) => f.slice(0, -4));
assert.ok(ids.length > 0, `no .dsp files in ${dir}`);
assert.ok(ids.includes(NEUTRAL_INSTRUMENT_ID), `neutral instrument "${NEUTRAL_INSTRUMENT_ID}" is not a catalog id (${ids.join(", ")})`);
console.log(`ok: ${NEUTRAL_INSTRUMENT_ID} in catalog mirror {${ids.join(", ")}}`);
