// Threshold gate over a report. Rules come from thresholds.json: { group: all|patch|ding, id?: glob, metric, min?, max?,
// equals?, level?: fail|warn, why }. A waiver { id, metric, reason, followUp } turns a known failure into a printed
// 'waived' line, and a waiver that no longer matches a failing rule is itself a failure, so waivers get deleted when
// the thing they excuse is fixed. Thresholds are tripwires for a listener to check, not a judgement of the sound.
const glob = (pattern) => new RegExp("^" + pattern.replace(/[.+^${}()|[\]\\]/g, "\\$&").replace(/\*/g, ".*") + "$");
const get = (m, path) => path.split(".").reduce((o, k) => o?.[k], m);

function violation(rule, value) {
  if (value === undefined || value === null || Number.isNaN(value)) return rule.min !== undefined || rule.max !== undefined ? "no value" : null;
  if (rule.equals !== undefined && value !== rule.equals) return `${value} (want ${rule.equals})`;
  if (rule.max !== undefined && !(value <= rule.max)) return `${fmt(value)} (max ${rule.max})`;
  if (rule.min !== undefined && !(value >= rule.min)) return `${fmt(value)} (min ${rule.min})`;
  return null;
}
const fmt = (v) => (typeof v === "number" ? +v.toFixed(2) : v);

export function gate(report, thresholds, { partial = false } = {}) {
  const failures = [], warnings = [], waived = [], hits = new Set();
  const waivers = thresholds.waivers ?? [];
  for (const it of report.items) {
    if (it.error || it.metrics?.broken) { failures.push({ id: it.id, metric: "render", detail: it.error ?? `${it.metrics.nonFinite} non-finite samples`, why: "item did not render cleanly" }); continue; }
    for (const rule of thresholds.rules) {
      if (rule.group !== "all" && rule.group !== it.group) continue;
      if (rule.id && !glob(rule.id).test(it.id)) continue;
      const detail = violation(rule, get(it.metrics, rule.metric));
      if (!detail) continue;
      const entry = { id: it.id, metric: rule.metric, detail, why: rule.why };
      const w = waivers.findIndex((x) => x.metric === rule.metric && glob(x.id).test(it.id));
      if (rule.level === "warn") warnings.push(entry);
      else if (w >= 0) { hits.add(w); waived.push({ ...entry, reason: waivers[w].reason, followUp: waivers[w].followUp }); }
      else failures.push(entry);
    }
  }
  if (!partial) waivers.forEach((w, i) => { if (!hits.has(i)) failures.push({ id: w.id, metric: w.metric, detail: "waiver matches nothing failing", why: "stale waiver: delete it" }); });
  return { pass: failures.length === 0, items: report.items.length, failures, warnings, waived };
}

export function formatGate(g) {
  const line = (e) => `  ${e.id}  ${e.metric}: ${e.detail}  [${e.why}]${e.reason ? `\n      waived: ${e.reason}${e.followUp ? ` (${e.followUp})` : ""}` : ""}`;
  const out = [`gate: ${g.pass ? "PASS" : "FAIL"} (${g.items} items, ${g.failures.length} failing, ${g.waived.length} waived, ${g.warnings.length} warnings)`];
  for (const [title, list] of [["FAILING", g.failures], ["WAIVED", g.waived], ["WARNINGS", g.warnings]]) if (list.length) out.push(title, ...list.map(line));
  out.push("", "Thresholds are tripwires for a listener to check. Passing means nothing was heard to be wrong; it does not mean it sounds right.");
  return out.join("\n");
}
