#!/usr/bin/env python3
"""gen_page.py <publishDir> : writes <publishDir>/index.html (static, no autoplay, no script needed) from <publishDir>/metrics.json, findings.json and voices.json.
<publishDir>/out holds the v3 files (senders/, proposals/, neutral/) and out/v2/senders the v2 files they are heard beside."""
import json, html, os, sys
root = sys.argv[1]; here = os.path.dirname(os.path.abspath(__file__))
M = json.load(open(os.path.join(root, "metrics.json"))); F = json.load(open(os.path.join(root, "findings.json"))); V = json.load(open(os.path.join(root, "voices.json")))["voices"]
MOTIFS = ["mention", "dm", "message", "invite"]
RHY = {"mention": "one note", "dm": "two slower notes, rising", "message": "two quick notes, rising", "invite": "a long pause, then a short one: three notes"}
clip = {(r["sender"], r["motif"]): r for r in M["clips"] + M["proposals"]}; neu = {n["motif"]: n for n in M["neutral"]}; S = M["summary"]
e = html.escape
def aud(src, label, role="voice", match=None):
    d = f' data-match="{match:.4f}"' if match else ""
    return f'<audio controls preload="none" src="{src}" aria-label="{e(label)}" data-role="{role}"{d}></audio>'
def ms(s, m): return f'<div class="sub">{clip[(s, m)]["durationMs"]:.0f} ms</div>'
def table(cols, caption):  # cols: [(header, fn(m) -> cell html)]
    head = "".join(f'<th scope="col">{e(h)}</th>' for h, _ in cols)
    body = "".join(f'<tr><th scope="row">{m}<br><span class="sub">{e(RHY[m])}</span></th>' + "".join(f"<td>{fn(m)}</td>" for _, fn in cols) + "</tr>" for m in MOTIFS)
    return f'<table><caption class="sr">{e(caption)}</caption><thead><tr><th scope="col">Notification</th>{head}</tr></thead><tbody>{body}</tbody></table>'
def beside(s, title, note):
    v2 = lambda m: aud(f"out/v2/senders/{s}/{m}.ogg", f"{s} {m}, v2")
    v3 = lambda m: aud(f"out/senders/{s}/{m}.ogg", f"{s} {m}, v3") + ms(s, m)
    return f'<section aria-labelledby="h-{s}"><h2 id="h-{s}">{title} <span class="sub">{e(V[s]["object"])}</span></h2><p class="caption"><strong>Meant to sound like (a hypothesis):</strong> {e(V[s]["caption"])}</p>{note}' + table([("v3 (new, unheard)", v3), ("v2 (as listened to)", v2)], f"{s}: v3 beside v2") + "</section>"
def nibu():
    cols = [(f"Proposal {i}", (lambda p: lambda m: aud(f"out/proposals/{p}/{m}.ogg", f"Nibu proposal {p[-1]}, {m}") + ms(p, m))(f"nibu{i}")) for i in (1, 2, 3)]
    cols.append(("v2 (rejected)", lambda m: aud(f"out/v2/senders/nibu/{m}.ogg", f"Nibu v2, {m}")))
    caps = "".join(f'<li><strong>Proposal {i}: {e(V[f"nibu{i}"]["family"])}.</strong> {e(V[f"nibu{i}"]["caption"])}</li>' for i in (1, 2, 3))
    return f'<section aria-labelledby="h-nibu"><h2 id="h-nibu">Nibu: three proposals <span class="sub">the one openly synthetic voice; the saw glide is gone from all three</span></h2><ol class="caps">{caps}</ol><p>Nibu and Meta choose. The manifest points Nibu at proposal 1 only as a placeholder.</p>' + table(cols, "Nibu: three proposals and v2") + "</section>"
def f1(x): return "—" if x is None else f"{x}"
void = F["void"]["mention"]; lib = F["libby"]; k = F["kiko"]
find = f'''<section aria-labelledby="h-find"><h2 id="h-find">What was found <span class="sub">numbers are measurements of the decoded files, not verdicts</span></h2><ul>
<li><strong>Void.</strong> The release does not clip: 0 samples at or above 0.999 full scale in any Void clip (v2 and v3); the last 50 ms of v2 peak at {void["v2"]["last50TruePeakDbTP"]} dBTP (mention), {F["void"]["dm"]["v2"]["last50TruePeakDbTP"]} (dm), {F["void"]["message"]["v2"]["last50TruePeakDbTP"]} (message), {F["void"]["invite"]["v2"]["last50TruePeakDbTP"]} (invite), and every clip's true peak is under {max(F["void"][m]["v2"]["clipTruePeakDbTP"] for m in MOTIFS)} dBTP. The tone's own envelope ends clean (about -35 dB re its peak, then a 15 ms fade). What does click is the "dry tick" riding the release: it had a 0.5 ms attack and its 5 ms peak stood 3.3 dB over the tone around it (-12.4 against -15.7 and -16.4 dB re the clip peak), a {void["v2"]["maxStepVsMedianStepX"]}x jump in sample-to-sample step at {void["v2"]["maxStepAtMs"]} ms. In v3 the tick is a soft knock (4 ms attack, 5 ms peak -16.0 dB, level with the tone instead of over it): that step is {void["v3"]["maxStepVsMedianStepX"]}x, i.e. gone. That the tick is what you heard is a hypothesis; I did not hear it.</li>
<li><strong>Libby.</strong> The generator was already polyphonic in the sense you asked about: rendering each note of a Libby clip alone and summing them reproduces the clip to between -145 and -150 dB re its peak (float rounding), in every sender's dm, message and invite except Void's, so note 2 does not steal note 1's envelope, phase or filter, and there is no shared reverb or filter in the stack. (The one shared state was the noise stream, used only by Void's tick: Void's solo-vs-mixed diff was -21 to -19.5 dB. Now each note owns its own.) What the numbers do show: in v2 the tail of the previous note when the next one landed was {lib["invite"]["v2"]["notes"][1]["priorTailDbReClipPeak"]} and {lib["invite"]["v2"]["notes"][2]["priorTailDbReClipPeak"]} dB re the clip peak in invite (dm {lib["dm"]["v2"]["notes"][1]["priorTailDbReClipPeak"]}, message {lib["message"]["v2"]["notes"][1]["priorTailDbReClipPeak"]}), so invite overlapped itself; and every multi-note clip is matched to the same exposure as the single note, so each note sits 1.3-3.9 dB under the mention's peak. Either could read as muffled or swampy; <strong>that is a hypothesis until measured by ear, and nobody has heard v3.</strong> v3: a note something follows is cut to its gap and has settled to {lib["dm"]["v3"]["notes"][1]["priorTailDbReClipPeak"]} / {lib["invite"]["v3"]["notes"][1]["priorTailDbReClipPeak"]} / {lib["invite"]["v3"]["notes"][2]["priorTailDbReClipPeak"]} dB at the next onset; the last note rings (dm now {lib["dm"]["v3"]["durationMs"]:.0f} ms, message limited to {lib["message"]["v3"]["durationMs"]:.0f} by the 400 ms cap).</li>
<li><strong>Kiko.</strong> v2 was a 142 ms swell ({k["v2"]["onsetRiseMs"]} ms onset rise) on a pitch that rose 7% over the note (0.96 to 1.03 of nominal), with a detuned pair beating against itself. v3 is struck glass: onset rise {k["v3"]["onsetRiseMs"]} ms, no pitch movement, no noise layer, partials at ratios {" : ".join(str(p["ratio"]) for p in k["v3"]["partials"])} ({", ".join(str(p["dbRe1stPartial"]) + " dB" for p in k["v3"]["partials"][1:])} re the first). Against the other families: Libby's bell is 1 : {F["families"]["libby"]["partials"][1]["ratio"]} : {F["families"]["libby"]["partials"][2]["ratio"]} on 784 Hz with a {F["families"]["libby"]["onsetRiseMs"]} ms onset; Nibu's FM bell (proposal 2) is harmonic, 1 : {F["families"]["nibu2"]["partials"][1]["ratio"]} : {F["families"]["nibu2"]["partials"][2]["ratio"]}, on 277 Hz. Whether it reads as glass and not a balloon is a listening question.</li>
</ul></section>'''
unchanged = [s for s in ("aqua", "eureka", "heimdall", "weksa")]
def plain(s):
    rows = "".join(f'<tr><th scope="row">{m}<br><span class="sub">{e(RHY[m])}</span></th><td>{aud(f"out/senders/{s}/{m}.ogg", f"{s} {m}")}{ms(s, m)}</td><td>{aud(f"out/neutral/{m}.ogg", f"neutral {m}", "neutral", min(1.0, 10 ** ((clip[(s, m)]["laeDb"] - neu[m]["laeDb"]) / 20)))}</td></tr>' for m in MOTIFS)
    return f'<section aria-labelledby="h-{s}"><h2 id="h-{s}">{s.capitalize()} <span class="sub">{e(V[s]["object"])} · unchanged since v2</span></h2><p class="caption">{e(V[s]["caption"])}</p><table><caption class="sr">{s}: voice beside the neutral fallback</caption><thead><tr><th scope="col">Notification</th><th scope="col">Voice</th><th scope="col">Neutral fallback</th></tr></thead><tbody>{rows}</tbody></table></section>'
page = f'''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Ratatoskr dings: v3 changes (unheard)</title>
<style>
:root{{color-scheme:light dark;--fg:#1b1f23;--bg:#fbfbf8;--mut:#4a5159;--line:#c9ccd0;--acc:#0b5cad}}
@media (prefers-color-scheme:dark){{:root{{--fg:#e8eaed;--bg:#14171a;--mut:#aab0b8;--line:#3a4047;--acc:#7ab7ff}}}}
body{{margin:0 auto;max-width:60rem;padding:1rem;font:1.05rem/1.5 system-ui,sans-serif;color:var(--fg);background:var(--bg)}}
h1{{font-size:1.5rem}}h2{{font-size:1.2rem;margin-top:2rem}}.sub{{font-size:.9rem;color:var(--mut);font-weight:400}}
.notice{{border:2px solid var(--acc);padding:.75rem 1rem;border-radius:.4rem}}.caption{{margin:.25rem 0 .75rem}}
table{{border-collapse:collapse;width:100%}}th,td{{border-top:1px solid var(--line);padding:.4rem .5rem;text-align:left;vertical-align:top}}
audio{{width:100%;min-width:10rem}}a,summary{{color:var(--acc)}}:focus-visible{{outline:3px solid var(--acc);outline-offset:2px}}.sr{{position:absolute;left:-9999px}}label{{display:block;margin:.5rem 0}}
</style></head><body>
<header><h1>Ratatoskr dings: what changed after v2 (v3)</h1>
<p class="notice"><strong>Nobody has heard v3.</strong> v2 was listened to by the people whose notes started this round; v3 by no one, its author included. The diagnosis for Libby is a hypothesis until measured by ear, and the fix for Void and the Kiko rework are hypotheses of the same kind. Nothing plays until you press play. The clips are quiet (at most {S["maxTruePeakDbTP"]} dBTP), so start with your volume low. Not installed into Ratatoskr.</p></header>
<main>
{find}
{nibu()}
{beside("void", "Void", "<p>The tick riding the release is softer; the tone is unchanged.</p>")}
{beside("libby", "Libby", "<p>A note that something follows now ends by the next onset; the last note rings.</p>")}
{beside("kiko", "Kiko", "<p>Reworked from bowed glass to struck glass after Deru's note that v2 sounded like a squeaking balloon.</p>")}
<h2>Unchanged voices</h2><p class="sub">Same source as v2. Their level moved by 0.1 dB or less because the shared loudness target moved with Void's quieter tick.</p>
<label><input type="checkbox" id="match" checked> Play the neutral fallback at the same A-weighted loudness as the voice beside it (the staged neutral files are louder than the voices). Some devices ignore the per-element volume this uses; the neutral then plays louder.</label>
{"".join(plain(s) for s in unchanged)}
</main>
<script>
(function(){{
  var all=[].slice.call(document.querySelectorAll("audio")), match=document.getElementById("match");
  function level(a){{a.volume=(a.dataset.role==="neutral"&&match.checked)?parseFloat(a.dataset.match):1}}
  all.forEach(function(a){{level(a);a.addEventListener("play",function(){{level(a);all.forEach(function(o){{if(o!==a)o.pause()}})}})}});
  match.addEventListener("change",function(){{all.forEach(level)}});
}})();
</script></body></html>'''
open(os.path.join(root, "index.html"), "w").write(page); print("wrote index.html", len(page))
