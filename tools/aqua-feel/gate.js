// Threshold gate over a report. Rules come from thresholds.json: { group: all|patch|ding, id?: glob, metric, min?, max?,
// equals?, level?: fail|warn, why }. A waiver { id, metric, reason, followUp, max?|min? } turns a known failure into a
// printed 'waived' line, but only up to the bound it states: a numeric rule needs a waiver bound on the same side
// (max for a max rule, min for a min rule) and the value must stay inside it, so a waived item cannot get worse unseen.
// A waiver that matches nothing failing, and a rule with an `id` that matches no item, are failures on a full run, so
// waivers get deleted when the thing they excuse is fixed and a catalog rename cannot silently disarm a guard.
// Thresholds are tripwires for a listener to check, not a judgement of the sound.
const glob = (pattern) => new RegExp("^" + pattern.replace(/[.+^${}()|[\]\\]/g, "\\$&").replace(/\*/g, ".*") + "$");
const get = (m, path) => path.split(".").reduce((o, k) => o?.[k], m);
const fmt = (v) => (typeof v === "number" ? +v.toFixed(2) : v);

function violation(rule, value) {
  if (value === undefined || value === null || Number.isNaN(value)) return "no value";
  if (rule.equals !== undefined && value !== rule.equals) return `${value} (want ${rule.equals})`;
  if (rule.max !== undefined && !(value <= rule.max)) return `${fmt(value)} (max ${rule.max})`;
  if (rule.min !== undefined && !(value >= rule.min)) return `${fmt(value)} (min ${rule.min})`;
  return null;
}

// Does waiver `w` cover this violation? Boolean/equals rules: any waiver for the metric. Numeric rules: the waiver must
// state a bound on the violated side and the value must lie within it.
function covers(w, rule, value) {
  if (rule.equals !== undefined) return true;
  if (typeof value !== "number" || Number.isNaN(value)) return false;
  if (rule.max !== undefined && !(value <= rule.max)) return typeof w.max === "number" && value <= w.max;
  if (rule.min !== undefined && !(value >= rule.min)) return typeof w.min === "number" && value >= w.min;
  return false;
}

export function gate(report, thresholds, { partial = false } = {}) {
  const failures = [], warnings = [], waived = [], hits = new Set(), armed = new Set();
  const waivers = thresholds.waivers ?? [];
  for (const it of report.items) {
    if (it.error || it.metrics?.broken) { failures.push({ id: it.id, metric: "render", detail: it.error ?? `${it.metrics.nonFinite} non-finite samples`, why: "item did not render cleanly" }); continue; }
    const silent = it.metrics?.silent === true;
    if (silent) failures.push({ id: it.id, metric: "silent", detail: "peak under -70 dBFS", why: "an item that renders nothing" });
    thresholds.rules.forEach((rule, ri) => {
      if (rule.group !== "all" && rule.group !== it.group) return;
      if (rule.id && !glob(rule.id).test(it.id)) return;
      armed.add(ri);
      if (silent) return; // reported once above; its other metrics do not exist
      const value = get(it.metrics, rule.metric), detail = violation(rule, value);
      if (!detail) return;
      const entry = { id: it.id, metric: rule.metric, detail, why: rule.why };
      if (rule.level === "warn") { warnings.push(entry); return; }
      const w = waivers.findIndex((x) => x.metric === rule.metric && glob(x.id).test(it.id) && covers(x, rule, value));
      if (w >= 0) { hits.add(w); waived.push({ ...entry, reason: waivers[w].reason, followUp: waivers[w].followUp }); }
      else failures.push(entry);
    });
  }
  if (!partial) {
    thresholds.rules.forEach((r, i) => { if (r.id && !armed.has(i)) failures.push({ id: r.id, metric: r.metric, detail: "rule matches no item", why: "disarmed guard: the item was renamed or dropped" }); });
    waivers.forEach((w, i) => { if (!hits.has(i)) failures.push({ id: w.id, metric: w.metric, detail: "waiver matches nothing failing within its bound", why: "stale waiver: delete it (or the item got worse than the bound)" }); });
  }
  return { pass: failures.length === 0, items: report.items.length, partial, failures, warnings, waived };
}

export function formatGate(g) {
  const line = (e) => `  ${e.id}  ${e.metric}: ${e.detail}  [${e.why}]${e.reason ? `\n      waived: ${e.reason}${e.followUp ? ` (${e.followUp})` : ""}` : ""}`;
  const out = [`gate: ${g.pass ? "PASS" : "FAIL"} (${g.items} items${g.partial ? ", partial run: id-rule and waiver bookkeeping skipped" : ""}, ${g.failures.length} failing, ${g.waived.length} waived, ${g.warnings.length} warnings)`];
  for (const [title, list] of [["FAILING", g.failures], ["WAIVED", g.waived], ["WARNINGS", g.warnings]]) if (list.length) out.push(title, ...list.map(line));
  out.push("", "Thresholds are tripwires for a listener to check. Passing means nothing was heard to be wrong; it does not mean it sounds right.");
  return out.join("\n");
}
