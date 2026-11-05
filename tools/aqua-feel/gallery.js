// Static HTML gallery from a report: no scripts, no network. Each card has the WAV (native audio controls, for a human
// to press), an SVG level envelope, an SVG spectral-centroid curve, and the metric table with gate failures marked.
// The page says on its face that nothing was heard.
const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);
const num = (v, d = 1) => (typeof v === "number" && Number.isFinite(v) ? v.toFixed(d) : "–");

function plot(points, { w = 260, h = 56, ymin, ymax, log = false, color }) {
  if (!points?.length) return `<svg width="${w}" height="${h}"></svg>`;
  const tmax = points[points.length - 1].t || 1, ys = points.map((p) => (log ? Math.log2(Math.max(p.v, 20)) : p.v));
  const lo = ymin ?? Math.min(...ys), hi = ymax ?? Math.max(...ys), span = hi - lo || 1;
  const path = points.map((p, i) => `${i ? "L" : "M"}${((p.t / tmax) * (w - 2) + 1).toFixed(1)},${(h - 2 - ((ys[i] - lo) / span) * (h - 4)).toFixed(1)}`).join("");
  return `<svg width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" role="img"><rect width="${w}" height="${h}" fill="#10161c"/><path d="${path}" fill="none" stroke="${color}" stroke-width="1.5"/></svg>`;
}

const ROWS = [
  ["onset ms", "onsetMs", 2], ["attack ms (10→90%, first peak)", "attackMs", 2], ["attack slope dB/ms", "attackSlopeDbPerMs", 1],
  ["sample peak", "samplePeak", 3], ["true peak dBTP", "truePeakDb", 1], ["over-1.0 samples", "overSamples", 0], ["flat-top runs", "flatTopRuns", 0],
  ["loudness LUFS", "loudnessLufs", 1], ["crest dB", "crestDb", 1], ["centroid median Hz", "centroidMedianHz", 0], ["flux mean", "fluxMean", 3],
  ["noise-to-tone dB (flat)", "noiseToToneDb", 1], ["noise-to-tone dB (A-weighted)", "noiseToToneADb", 1], ["level range dB", "envRangeDb", 1], ["centroid spread (oct)", "centroidSpreadOct", 2],
  ["active ms", "activeMs", 0], ["low motion", "lowMotion", 0],
];

export function gallery(report) {
  const bad = new Map();
  for (const [kind, list] of [["fail", report.gate?.failures ?? []], ["waived", report.gate?.waived ?? []], ["warn", report.gate?.warnings ?? []]])
    for (const e of list) { const k = `${e.id}|${e.metric}`; if (!bad.has(k) || kind === "fail") bad.set(k, kind); }
  const cards = report.items.map((it) => {
    const m = it.metrics;
    if (!m) return `<section class="card fail"><h3>${esc(it.id)}</h3><p>did not render: ${esc(it.error ?? "unknown")}</p></section>`;
    const rows = ROWS.map(([label, key, d]) => {
      const v = m[key], cls = bad.get(`${it.id}|${key}`) ?? "", shown = typeof v === "boolean" ? String(v) : num(v, d);
      return `<tr class="${cls}"><th>${label}</th><td>${shown}${cls ? ` <b>${cls}</b>` : ""}</td></tr>`;
    }).join("");
    const worst = ["fail", "waived", "warn"].find((k) => [...bad].some(([key, v]) => key.startsWith(it.id + "|") && v === k)) ?? "";
    return `<section class="card ${worst}" id="${esc(it.id)}"><h3>${esc(it.id)} <small>${it.group}</small></h3>
<audio controls preload="none" src="${esc(it.wav)}"></audio>
<p class="cap">level envelope (dB, ${num(m.envelopeCurve?.at(-1)?.t, 1)} s)</p>${plot(m.envelopeCurve, { ymin: -90, ymax: 6, color: "#6fb1c7" })}
<p class="cap">spectral centroid (log Hz)</p>${plot(m.centroidCurve, { log: true, ymin: Math.log2(20), ymax: Math.log2(16000), color: "#c7a46f" })}
<table>${rows}</table><p class="cap">${esc(it.origin ?? "")}</p></section>`;
  });
  const g = report.gate;
  return `<!doctype html><meta charset="utf-8"><title>aqua-feel gallery</title>
<style>body{font:14px/1.4 system-ui,sans-serif;background:#0b0f13;color:#d8dee4;margin:1.5rem}h1{font-size:1.3rem}h3{margin:.2rem 0;font-size:.95rem}small{color:#7d8a96;font-weight:400}
.banner{border:1px solid #c7a46f;padding:.7rem 1rem;margin:1rem 0;color:#f0e2c4}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:12px}
.card{background:#141b22;border:1px solid #232d37;padding:10px}.card.fail{border-color:#d4574a}.card.waived{border-color:#c7a46f}.card.warn{border-color:#8a7f4a}
table{border-collapse:collapse;width:100%;margin-top:6px}th{text-align:left;font-weight:400;color:#8c99a5;padding:1px 6px 1px 0}td{padding:1px 0}tr.fail td{color:#ff8a7d}tr.waived td{color:#e0c07f}tr.warn td{color:#c9be85}
.cap{margin:6px 0 2px;color:#7d8a96;font-size:.75rem}audio{width:100%;height:30px}b{font-size:.7rem;text-transform:uppercase}</style>
<h1>aqua-feel gallery</h1>
<div class="banner"><b>Nothing here was heard.</b> Every number is a measurement of an offline-rendered buffer (faustwasm, mono channel 0, ${report.sampleRate} Hz, libfaust ${esc(report.libfaust)}). Metrics are evidence for a listener to check, not verdicts; the WAVs are there so a person can press play. Gate: <b>${g?.pass ? "pass" : "fail"}</b> (${g?.failures.length ?? 0} failing, ${g?.waived.length ?? 0} waived, ${g?.warnings.length ?? 0} warnings).</div>
<div class="grid">${cards.join("\n")}</div>`;
}
