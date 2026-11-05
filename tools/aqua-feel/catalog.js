// What aqua-feel renders: every playground patch, the thruster at four rpm, and every Ding event on every instrument.
import { join } from "node:path";
import { readText, playground } from "./render.js";
import { dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
export const THRUSTER_RPM = [300, 600, 1200, 4800];
export const PATCH_SECONDS = 3, DING_COMMAND_GAIN = 0.6;

export function patchItems() {
  const dir = join(playground, "lowered"), cat = JSON.parse(readText(join(dir, "catalog.json"))), items = [];
  for (const e of cat) {
    const source = readText(join(dir, e.dsp));
    if (e.name === "thruster") for (const rpm of THRUSTER_RPM) items.push({ id: `thruster@rpm${rpm}`, group: "patch", origin: e.origin, source, params: { rpm }, seconds: PATCH_SECONDS });
    else items.push({ id: e.name, group: "patch", origin: e.origin, source, params: {}, seconds: PATCH_SECONDS });
  }
  return items;
}

export function dingItems() {
  const dir = join(here, "lowered-dings"), d = JSON.parse(readText(join(dir, "dings.json"))), items = [];
  for (const ins of d.instruments) for (const ev of d.events)
    items.push({ id: `ding/${ev.id}/${ins.id}`, group: "ding", origin: `DingCatalog ${ev.id} on ${ins.id}`, ding: { instrument: ins, event: ev, source: readText(join(dir, ins.dsp)), noteSeconds: d.noteSeconds, commandGain: DING_COMMAND_GAIN } });
  return items;
}
export const allItems = () => [...patchItems(), ...dingItems()];
