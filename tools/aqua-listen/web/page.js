// Page glue. The kit (manifest, content hash, base64 audio, optional base64 PNGs per clip id) is in #kit-data; logic is AquaListen (logic.cjs, inlined).
(() => {
  const L = globalThis.AquaListen, kit = JSON.parse(document.getElementById("kit-data").textContent);
  const app = document.getElementById("app"), KEY = L.saveKey(kit.manifest);
  let state = null, order = null;

  const save = () => { try { localStorage.setItem(KEY, JSON.stringify(state)); } catch (e) { /* private mode: the export still works */ } };
  const read = (k) => { try { return JSON.parse(localStorage.getItem(k)); } catch (e) { return null; } };
  const valid = (s) => (s && s.seed ? s : null);
  const legacyEntries = () => { const e = []; for (let i = 0; i < localStorage.length; i++) { const k = localStorage.key(i); e.push({ key: k, state: read(k) }); } return e; };
  // Own key first; if empty, adopt a hash-keyed save from an earlier build of this kit (L.pickLegacySave) and keep using the new key.
  const load = () => {
    const own = valid(read(KEY)); if (own) return own;
    try { const from = L.pickLegacySave(kit.manifest, legacyEntries()); return from ? valid(read(from)) : null; } catch (e) { return null; }
  };
  const h = (tag, attrs, ...kids) => {
    const e = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs || {})) { if (k === "class") e.className = v; else if (k.startsWith("on")) e.addEventListener(k.slice(2), v); else if (v !== false && v != null) e.setAttribute(k, v === true ? "" : v); }
    for (const k of kids.flat()) e.append(k);
    return e;
  };
  const blobUrl = (id) => {
    const a = kit.audio[id], bin = atob(a.b64), u = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) u[i] = bin.charCodeAt(i);
    return URL.createObjectURL(new Blob([u], { type: a.mime }));
  };

  function consent() {
    const box = h("input", { type: "checkbox", id: "consent" }), go = h("button", { disabled: true, onclick: start }, "Start listening");
    box.addEventListener("change", () => { go.disabled = !box.checked; });
    app.replaceChildren(
      h("h1", {}, kit.manifest.kit.title),
      h("div", { class: "card" },
        h("p", {}, h("strong", {}, "Before you start. ")),
        h("p", {}, `You will hear ${kit.manifest.clips.length} short sounds in a random order and say what you think of each. Your answers help decide which sounds get kept, fixed or dropped.`),
        h("ul", {},
          h("li", {}, "This page works on its own. It talks to no server, has no account and no tracking, and sends nothing anywhere."),
          h("li", {}, "Your answers are saved in this browser only, so you can close the tab and come back. Clearing your browser data erases them."),
          h("li", {}, "Nothing leaves your computer until you press Export, which shows your answers as text to copy, share or save. Sending those files to the person who gave you this page is your choice."),
          h("li", {}, "You can stay anonymous. A name or nickname is optional.")),
        h("p", {}, h("label", {}, box, " I understand, and I'm happy to take part.")),
        h("p", {}, h("label", {}, "Name or nickname (optional) ", h("input", { type: "text", id: "alias", maxlength: "60" }))),
        go));
  }

  function start() {
    state = load() || { seed: (crypto.getRandomValues(new Uint32Array(1))[0] || 1), answers: {}, pairs: {}, device: null, notes: "", listener: "" };
    state.consent = true; state.listener = document.getElementById("alias") ? document.getElementById("alias").value.trim() || state.listener : state.listener;
    save(); main();
  }

  const answered = (id) => { const a = state.answers[id]; return !!(a && a.verdict && a.landed); };
  function touch(id, patch) {
    const a = state.answers[id] = Object.assign({}, state.answers[id], patch);
    if (!a.device && state.device) a.device = state.device;
    save(); progress();
    const card = document.getElementById("clip-" + id); if (card) card.classList.toggle("done", answered(id));
  }
  function radios(name, values, current, onpick, labels) {
    return values.map((v) => { const r = h("input", { type: "radio", name, value: String(v) }); if (String(current) === String(v)) r.checked = true; r.addEventListener("change", () => onpick(v)); return h("label", {}, r, " ", labels ? labels[v] : String(v)); });
  }

  function clipCard(o, n) {
    const c = kit.manifest.clips.find((x) => x.id === o.id), a = state.answers[o.id] || {}, id = o.id;
    const audio = h("audio", { controls: true, preload: "none" }); audio.src = blobUrl(id);
    const reason = h("input", { type: "text", maxlength: "300", value: a.reason || "", placeholder: "One line: why?" });
    reason.addEventListener("input", () => touch(id, { reason: reason.value }));
    return h("section", { class: "card" + (answered(id) ? " done" : ""), id: "clip-" + id },
      h("h2", {}, o.slot ? `Sound ${o.slot}` : `Sound ${n}`),
      h("p", { class: "feel" }, `It should feel: ${c.feel}`),
      audio,
      (kit.images[id] || []).length ? h("details", {}, h("summary", {}, "Pictures of this sound (optional)"), kit.images[id].map((p) => h("img", { src: "data:image/png;base64," + p.b64, alt: p.label, class: "pic" }))) : [],
      h("div", { class: "row" }, h("span", {}, "Keep, fix or drop?"), radios("v-" + id, L.VERDICTS, a.verdict, (v) => touch(id, { verdict: v }))),
      h("div", { class: "row" }, h("span", {}, "Did it land? 1 = not at all, 5 = completely"), radios("l-" + id, [1, 2, 3, 4, 5], a.landed, (v) => touch(id, { landed: Number(v) }))),
      reason);
  }

  function main() {
    order = L.planOrder(kit.manifest.clips, state.seed);
    const byId = new Map(kit.manifest.clips.map((c) => [c.id, c]));
    const prog = h("span", { id: "progress" });
    const dev = h("span", {}, "I'm listening on: ", radios("device", L.DEVICES, state.device, (v) => { state.device = v; save(); }));
    const cards = []; let n = 0;
    for (let i = 0; i < order.length; i++) {
      const o = order[i];
      if (o.slot === "A") {
        const b = order[i + 1], key = `${o.id}|${byId.get(o.id).pair}`;
        const pick = h("div", { class: "row" }, h("span", {}, "Which do you prefer?"), radios("p-" + key, ["A", "B", "same"], state.pairs[key], (v) => { state.pairs[key] = v; save(); }, { A: "A", B: "B", same: "No difference" }));
        n++; cards.push(h("div", { class: "pair" }, h("h3", {}, `Pair ${n}: A and B`), clipCard(o, n), clipCard(b, n), pick)); i++;
      } else { n++; cards.push(clipCard(o, n)); }
    }
    const notes = h("textarea", { rows: "3", placeholder: "Anything else? (optional)" }, state.notes || ""); notes.addEventListener("input", () => { state.notes = notes.value; save(); });
    app.replaceChildren(
      h("h1", {}, kit.manifest.kit.title),
      kit.manifest.kit.note ? h("p", { class: "hint" }, kit.manifest.kit.note) : "",
      h("div", { class: "bar" }, h("div", { class: "row" }, dev, prog)),
      ...cards,
      h("div", { class: "card" }, h("h2", {}, "Finished?"), notes,
        h("p", { class: "hint" }, "Export shows your answers as text you can copy, share or download. Unanswered sounds are exported as blank. Your answers stay saved in this browser until you erase them."),
        h("div", { class: "row" }, h("button", { id: "export", onclick: exportAll }, "Export my answers"), h("button", { class: "quiet", onclick: reset }, "Erase my answers")), h("div", { id: "export-panel" })));
    progress();
  }

  function progress() { const e = document.getElementById("progress"); if (e) e.textContent = `${kit.manifest.clips.filter((c) => answered(c.id)).length} of ${kit.manifest.clips.length} answered`; }
  // Export is a visible panel, not a silent download: Android Chrome blocks the second of two scripted downloads and
  // gives no sign, so the verdict is also shown in a read-only textarea with copy, share and one-download-per-tap buttons.
  function verdictNow() { return L.buildVerdict(kit.manifest, order, state, new Date().toISOString()); }
  function download(name, text, type) {
    const a = h("a", { href: URL.createObjectURL(new Blob([text], { type })), download: name });
    document.body.append(a); a.click(); a.remove();
  }
  async function copyText(text, area) {
    try { await navigator.clipboard.writeText(text); return true; } catch (e) { /* no clipboard API or not allowed: fall through */ }
    try { area.focus(); area.select(); area.setSelectionRange(0, text.length); return document.execCommand("copy"); } catch (e) { return false; }
  }
  function exportAll() {
    const v = verdictNow(), json = JSON.stringify(v, null, 2) + "\n", md = L.verdictMarkdown(v);
    const area = h("textarea", { id: "export-text", rows: "10", readonly: true, "aria-label": "Your answers as verdict.json" }, json);
    const msg = h("p", { class: "hint", id: "export-msg", role: "status" }, "Your answers are below. Copy them and paste them into a message to whoever sent you this page.");
    const say = (t) => { msg.textContent = t; };
    const buttons = [
      h("button", { id: "copy-json", onclick: async () => say((await copyText(json, area)) ? "Copied verdict.json. Paste it into your message." : "Copy was blocked. The text box is selected: use your phone's Copy.") }, "Copy answers (JSON)"),
      h("button", { class: "quiet", id: "copy-md", onclick: async () => { const t = h("textarea", {}, md); document.body.append(t); const ok = await copyText(md, t); t.remove(); say(ok ? "Copied the readable table." : "Copy was blocked."); } }, "Copy as table"),
      h("button", { class: "quiet", onclick: () => { area.focus(); area.select(); area.setSelectionRange(0, json.length); say("Selected. Use your phone's Copy."); } }, "Select all"),
      h("button", { class: "quiet", onclick: () => download("verdict.json", json, "application/json") }, "Download verdict.json"),
      h("button", { class: "quiet", onclick: () => download("verdict.md", md, "text/markdown") }, "Download verdict.md")];
    if (navigator.share) buttons.splice(2, 0, h("button", { class: "quiet", id: "share", onclick: () => navigator.share({ title: "Listening verdict", text: json }).catch(() => say("Sharing was cancelled or unavailable.")) }, "Share"));
    const panel = document.getElementById("export-panel");
    panel.replaceChildren(h("h3", {}, "Your answers"), msg, area, h("div", { class: "row" }, buttons));
    area.focus(); area.select(); panel.scrollIntoView();
  }
  function reset() { if (confirm("Erase every answer you've given in this browser?")) { try { localStorage.removeItem(KEY); for (let k; (k = L.pickLegacySave(kit.manifest, legacyEntries())); ) localStorage.removeItem(k); } catch (e) {} location.reload(); } }

  if (load() && load().consent) start(); else consent();
})();
