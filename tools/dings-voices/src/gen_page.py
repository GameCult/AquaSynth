#!/usr/bin/env python3
"""gen_page.py: writes page/index.html (static, no scripts required, no autoplay) from metrics.json and src/voices.json."""
import json, html, os
here = os.path.dirname(os.path.abspath(__file__)); root = os.path.dirname(here)
M = json.load(open(os.path.join(root, "metrics.json"))); V = json.load(open(os.path.join(here, "voices.json")))["voices"]
SENDERS = ["aqua", "eureka", "heimdall", "kiko", "libby", "nibu", "void", "weksa"]
MOTIFS = ["mention", "dm", "message", "invite"]
RHY = {"mention": "one note", "dm": "two slower notes, rising", "message": "two notes, rising, spaced like a direct message", "invite": "a long pause, then a short one: three notes, rising"}
clip = {(r["sender"], r["motif"]): r for r in M["clips"]}; neu = {n["motif"]: n for n in M["neutral"]}
e = html.escape
def row(s, m):
    c, n = clip[(s, m)], neu[m]; gain = min(1.0, 10 ** ((c["laeDb"] - n["laeDb"]) / 20))
    return f'''<tr><th scope="row">{m}<br><span class="sub">{e(RHY[m])}</span></th>
<td><audio controls preload="none" src="../out/senders/{s}/{m}.ogg" aria-label="{s}, {m}: {e(V[s]["object"])}" data-role="voice"></audio><div class="sub">{c["durationMs"]:.0f} ms</div></td>
<td><audio controls preload="none" src="../out/neutral/{m}.ogg" aria-label="neutral fallback for {m}" data-role="neutral" data-match="{gain:.4f}"></audio><div class="sub">{n["durationMs"]:.0f} ms</div></td></tr>'''
secs = "\n".join(f'''<section aria-labelledby="h-{s}"><h2 id="h-{s}">{s.capitalize()} <span class="sub">{e(V[s]["object"])} · {e(V[s]["family"])}</span></h2>
<p class="caption"><strong>What it is meant to sound like:</strong> {e(V[s]["caption"])}</p>
<table><caption class="sr">{s}: the four notifications, voice beside the neutral fallback</caption><thead><tr><th scope="col">Notification</th><th scope="col">{s.capitalize()}'s voice</th><th scope="col">Neutral fallback</th></tr></thead><tbody>
{"".join(row(s, m) for m in MOTIFS)}</tbody></table></section>''' for s in SENDERS)
mt = "\n".join(f'<tr><th scope="row">{r["sender"]}</th>' + "".join(f'<td>{clip[(r["sender"], m)]["durationMs"]:.0f}</td>' for m in MOTIFS) + f'<td>{r["onsetRiseMs"]}</td><td>{r["centroidHz"]}</td><td>{r["flatnessDb"]}</td><td>{r["laeDb"]}</td><td>{r["truePeakDbTP"]}</td></tr>' for r in M["clips"] if r["motif"] == "mention")
S = M["summary"]
page = f'''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Ratatoskr dings: eight sender voices, v2 (unheard)</title>
<style>
:root{{color-scheme:light dark;--fg:#1b1f23;--bg:#fbfbf8;--mut:#4a5159;--line:#c9ccd0;--acc:#0b5cad}}
@media (prefers-color-scheme:dark){{:root{{--fg:#e8eaed;--bg:#14171a;--mut:#aab0b8;--line:#3a4047;--acc:#7ab7ff}}}}
body{{margin:0 auto;max-width:56rem;padding:1rem;font:1.05rem/1.5 system-ui,sans-serif;color:var(--fg);background:var(--bg)}}
h1{{font-size:1.5rem}}h2{{font-size:1.2rem;margin-top:2rem}}.sub{{font-size:.9rem;color:var(--mut);font-weight:400}}
.notice{{border:2px solid var(--acc);padding:.75rem 1rem;border-radius:.4rem}}.caption{{margin:.25rem 0 .75rem}}
table{{border-collapse:collapse;width:100%}}th,td{{border-top:1px solid var(--line);padding:.4rem .5rem;text-align:left;vertical-align:top}}
audio{{width:100%;min-width:12rem}}a,summary{{color:var(--acc)}}:focus-visible{{outline:3px solid var(--acc);outline-offset:2px}}
.sr{{position:absolute;left:-9999px}}label{{display:block;margin:.5rem 0}}
</style></head><body>
<header><h1>Ratatoskr dings: eight sender voices (v2)</h1>
<p class="notice"><strong>Nobody has heard these (v2 included).</strong> This revision answers one listener's notes on v1: longer mentions, a softer and more settled Eureka, a redone Heimdall horn, and message notes spaced apart. Whether it fixed any of that is a listening question. This page exists so a person can listen. The numbers below are measurements of the files; they are not verdicts, and none of them says a sound is pleasant, clear in your room, or reads as its object. Nothing plays until you press play. The clips are quiet (about {S["maxTruePeakDbTP"]} dBTP at most), so start with your volume low and raise it until you can hear them.</p></header>
<main>
<p>Each voice is a different small object. Each plays the same four notifications on a shared rhythm family (the rhythm tells the event, the voice tells who sent it): <em>mention</em> is one note, <em>message</em> two spaced notes, <em>dm</em> two slightly slower, wider notes, <em>invite</em> a long pause then a short one. The right-hand column is the neutral fallback Ratatoskr plays for any sender without a voice. Things to listen for: can you tell the eight voices apart; does each read as its named object; can you tell the four notifications apart within one voice; is anything startling, sharp or tiring if heard fifty times a day?</p>
<noscript><p class="notice">Without JavaScript the neutral fallback plays at its staged level, which is up to 14 dB louder than the voices. Lower your volume before playing it.</p></noscript>
<label><input type="checkbox" id="match" checked> Play the neutral fallback at the same A-weighted loudness as the voice beside it (the staged neutral files are louder than the voices). Untick to hear them as staged. (This uses the browser's per-element volume; some devices, iOS among them, ignore it, and then the neutral plays at its staged, louder level.)</label>
{secs}
<section aria-labelledby="h-metrics"><h2 id="h-metrics">Measurements (not verdicts)</h2>
<p>Measured on the decoded Ogg files. Loudness is A-weighted sound exposure, matched across all 32 clips to {S["targetLaeDb"]} dB re full scale (spread {S["laeSpreadDb"]} dB); the loudest true peak is {S["maxTruePeakDbTP"]} dBTP against a limit of {S["peakLimitDbTP"]} dBTP. The longest message clip is {S["maxMessageMs"]:.0f} ms. Onset rise is the time for the 1 ms RMS envelope to climb from 10% to 90% of its peak on the single-note clip (small means sharp). Details and caveats: <code>README.md</code>, <code>metrics.json</code>.</p>
<details><summary>Per-voice table (the single-note mention clip)</summary><div style="overflow-x:auto"><table><thead><tr><th scope="col">Voice</th><th scope="col">mention ms</th><th scope="col">dm ms</th><th scope="col">message ms</th><th scope="col">invite ms</th><th scope="col">onset rise ms</th><th scope="col">centroid Hz</th><th scope="col">flatness dB</th><th scope="col">LAE dB</th><th scope="col">true peak dBTP</th></tr></thead><tbody>{mt}</tbody></table></div></details></section>
</main>
<script>
(function(){{
  var all=[].slice.call(document.querySelectorAll("audio")), match=document.getElementById("match");
  function level(a){{a.volume=(a.dataset.role==="neutral"&&match.checked)?parseFloat(a.dataset.match):1}}
  all.forEach(function(a){{level(a);a.addEventListener("play",function(){{level(a);all.forEach(function(o){{if(o!==a)o.pause()}})}})}});
  match.addEventListener("change",function(){{all.forEach(level)}});
}})();
</script></body></html>'''
os.makedirs(os.path.join(root, "page"), exist_ok=True)
open(os.path.join(root, "page", "index.html"), "w").write(page)
print("wrote page/index.html", len(page))
