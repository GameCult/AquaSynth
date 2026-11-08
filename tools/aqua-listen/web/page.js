// Page glue. The kit (manifest, content hash, base64 audio) is in #kit-data; logic is AquaListen (logic.cjs, inlined).
(() => {
  const L = globalThis.AquaListen, kit = JSON.parse(document.getElementById("kit-data").textContent);
  const app = document.getElementById("app"), KEY = `aqua-listen:${kit.manifest.kit.id}:${kit.hash}`;
  let state = null, order = null;

  const save = () => { try { localStorage.setItem(KEY, JSON.stringify(state)); } catch (e) { /* private mode: the export still works */ } };
  const load = () => { try { const s = JSON.parse(localStorage.getItem(KEY)); return s && s.seed ? s : null; } catch (e) { return null; } };
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
          h("li", {}, "Nothing leaves your computer until you press Export, which saves two files. Sending those files to the person who gave you this page is your choice."),
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
        h("p", { class: "hint" }, "Export saves verdict.json and verdict.md to your downloads. Unanswered sounds are exported as blank."),
        h("div", { class: "row" }, h("button", { onclick: exportAll }, "Export verdict.json + verdict.md"), h("button", { class: "quiet", onclick: reset }, "Erase my answers"))));
    progress();
  }

  function progress() { const e = document.getElementById("progress"); if (e) e.textContent = `${kit.manifest.clips.filter((c) => answered(c.id)).length} of ${kit.manifest.clips.length} answered`; }
  function download(name, text, type) {
    const a = h("a", { href: URL.createObjectURL(new Blob([text], { type })), download: name });
    document.body.append(a); a.click(); a.remove();
  }
  function exportAll() {
    const v = L.buildVerdict(kit.manifest, order, state, new Date().toISOString());
    download("verdict.json", JSON.stringify(v, null, 2) + "\n", "application/json");
    download("verdict.md", L.verdictMarkdown(v), "text/markdown");
  }
  function reset() { if (confirm("Erase every answer you've given in this browser?")) { try { localStorage.removeItem(KEY); } catch (e) {} location.reload(); } }

  if (load() && load().consent) start(); else consent();
})();
