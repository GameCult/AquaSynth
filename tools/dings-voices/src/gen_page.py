#!/usr/bin/env python3
"""gen_page.py <publishDir> : writes <publishDir>/index.html (static, no autoplay, no script needed beyond the loudness-match checkbox) from <publishDir>/metrics.json,
voices.json, findings/onsets-{before,after}.json and findings/nibu-after.json (inspect_clips.py output). <publishDir>/out holds the v4 files (senders/, proposals/, neutral/)
and out/v3/senders/{void,nibu} the v3 files they are heard beside; <publishDir>/findings holds the PNGs."""
import json, html, os, sys
root = sys.argv[1]
J = lambda *p: json.load(open(os.path.join(root, *p)))
M = J("metrics.json"); V = J("voices.json")["voices"]; OB = J("findings", "onsets-before.json"); OA = J("findings", "onsets-after.json"); NB = J("findings", "nibu-after.json")
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
def evs(o): return ", ".join(str(i) for i in o["events"])
# what each pre-fix onset was, from the one-suspect-removed renders (see "How it was found")
CAUSE = {"mention": {193: "tick (note's own knock, 190 ms in; the mention's note is stretched 2x)"},
         "dm": {98: "tick of note 1", 339: "tick of note 2"}, "message": {98: "tick of note 1", 329: "tick of note 2"},
         "invite": {98: "tick of note 1", 309: "note 3's attack (with note 2's tick, ~295 ms, folded into it)", 398: "tick of note 3"}}
def void_rows():
    rows = []
    for m in MOTIFS:
        notes = OB[m]["noteMs"]; ons = OB[m]["events"]
        cells = []
        for i in ons:
            near = [n for n in notes if 0 <= i - n < 20]
            cells.append(f'{i} ms: ' + (f'note {notes.index(near[0]) + 1} attack' if near and i not in CAUSE[m] else e(CAUSE[m].get(i, "?"))))
        rows.append(f'<tr><th scope="row">{m}<br><span class="sub">notes at {", ".join(map(str, notes))} ms</span></th><td>{"<br>".join(cells)}</td><td>{evs(OA[m])}</td></tr>')
    return "".join(rows)
def void_section():
    v3 = lambda m: aud(f"out/v3/senders/void/{m}.ogg", f"void {m}, v3")
    v4 = lambda m: aud(f"out/senders/void/{m}.ogg", f"void {m}, v4") + ms("void", m)
    img = "".join(f'<figure><figcaption>{m}: before (v3) then after (v4)</figcaption><a href="findings/void-{m}-before.png"><img alt="void {m} before: spectrogram, waveform and 1 ms envelope" src="findings/void-{m}-before.png" loading="lazy"></a> <a href="findings/void-{m}-after.png"><img alt="void {m} after" src="findings/void-{m}-after.png" loading="lazy"></a></figure>' for m in MOTIFS)
    return f'''<section aria-labelledby="h-void"><h2 id="h-void">Void: the double hit <span class="sub">{e(V["void"]["object"])}</span></h2>
<p class="caption"><strong>Meant to sound like (a hypothesis):</strong> {e(V["void"]["caption"])}</p>
<p>Meta heard a weird double hit in v3. In every v3 Void note the spectrogram shows a second event 95 ms after the note starts (190 ms in the mention): a short broadband knock, the "soft tick" v3 kept on the tail of every note, level with the tone it sits on, so the loudness envelope barely shows it but the spectrum does. Removing only the tick removes every extra onset except one: in invite, note 2 (gap 100 ms) was still ringing at -16 dB when note 3 struck, which makes a second rise (+19 dB, 9 ms later +8.5 dB) in the envelope as the two tones beat; note 3 rendered alone has exactly one onset. v4 removes the tick and ends a note that something follows by the next onset, as Libby's do.</p>
<table><caption class="sr">Void onsets before and after</caption><thead><tr><th scope="col">Clip</th><th scope="col">v3 onsets (ms) and what made each</th><th scope="col">v4 onsets (ms)</th></tr></thead><tbody>{void_rows()}</tbody></table>
<p class="sub">An onset is a rise of the 1 ms envelope of at least 6 dB over the quietest point of the previous 15 ms (full band), or of 10 dB in the band above 1.5 kHz, where a click or knock shows even when it is level with the tone; events within 20 ms are one. v4 has exactly one per note.</p>
{table([("v4 (new, unheard)", v4), ("v3 (as listened to)", v3)], "void: v4 beside v3")}
<details open><summary>Spectrograms and envelopes (click for full size)</summary>{img}</details></section>'''
def nibu_section():
    cols = [("Proposal 3 (Nibu's voice, all four events)", lambda m: aud(f"out/senders/nibu/{m}.ogg", f"Nibu, proposal 3, {m}") + ms("nibu", m))]
    for i in (1, 2): cols.append((f"Proposal {i} (alternate, not in the manifest)", (lambda p: lambda m: aud(f"out/proposals/{p}/{m}.ogg", f"Nibu alternate proposal {p[-1]}, {m}") + ms(p, m))(f"nibu{i}")))
    cols.append(("v3 manifest voice (proposal 1)", lambda m: aud(f"out/v3/senders/nibu/{m}.ogg", f"Nibu v3, {m}")))
    caps = "".join(f'<li><strong>Proposal {i}: {e(V[f"nibu{i}"]["family"])}.</strong> {e(V[f"nibu{i}"]["caption"])}</li>' for i in (3, 1, 2))
    def note_cells(m): return "; ".join("note %d: %s dB above 4 kHz, %s dB at its end" % (i + 1, n["above4kHzDb"], n["last5msDbRePeak"]) for i, n in enumerate(NB[m]["notes"]))
    mult = "".join(f'<tr><th scope="row">{m}</th><td>{note_cells(m)}</td><td>{NB[m]["imageBandPeakDbRePeak"]} dB</td></tr>' for m in ("message", "dm", "invite"))
    return f'''<section aria-labelledby="h-nibu"><h2 id="h-nibu">Nibu: proposal 3 for every event <span class="sub">chosen by Nibu after Meta liked it; same motif rhythms as the other voices</span></h2><ol class="caps">{caps}</ol>
<p>Proposal 3 is Nibu's voice in the manifest for mention, message, dm and invite, at the shared rhythms (message two quick notes, dm two slow notes, invite three). Proposals 1 and 2 are kept here as alternates and are not in the manifest.</p>
{table(cols, "Nibu: proposal 3 beside the alternates and v3")}
<h3>Does proposal 3 survive more than one note?</h3>
<p>Measured on the raw renders, not by ear. Notes do not overlap: each note is silent (under -120 dB re its peak, checked as an exact test) by the next onset, and the clip is the sum of its per-note renders. No smear: the multi-note spectrograms show three clean steps and the 8 kHz sample-rate images (7.3 and 8.7 kHz for the invite's third note) as thin lines. Note 1 of every multi-note clip has the same spectrum as the single note (same pitch, first 200 ms, same peak at 349 Hz and the same energy above 4 kHz, -60.1 dB re total). The invite's high floor is more tones and more attacks, not aliasing: the strongest image line is {NB["invite"]["imageBandPeakDbRePeak"]} dB re the clip's peak (mention {NB["mention"]["imageBandPeakDbRePeak"]}, message {NB["message"]["imageBandPeakDbRePeak"]}, dm {NB["dm"]["imageBandPeakDbRePeak"]}).</p>
<table><caption class="sr">Nibu proposal 3 multi-note measurements</caption><thead><tr><th scope="col">Clip</th><th scope="col">Per note</th><th scope="col">Strongest line 6.5-9.5 kHz</th></tr></thead><tbody>{mult}</tbody></table>
<p><a href="findings/nibu-spectrum-after.png">spectrum, invite against mention</a> · <a href="findings/nibu-invite-after.png">invite spectrogram</a> · <a href="findings/nibu-message-after.png">message</a> · <a href="findings/nibu-dm-after.png">dm</a></p></section>'''
unchanged = ["aqua", "eureka", "heimdall", "kiko", "libby", "weksa"]
def plain(s):
    rows = "".join(f'<tr><th scope="row">{m}<br><span class="sub">{e(RHY[m])}</span></th><td>{aud(f"out/senders/{s}/{m}.ogg", f"{s} {m}")}{ms(s, m)}</td><td>{aud(f"out/neutral/{m}.ogg", f"neutral {m}", "neutral", min(1.0, 10 ** ((clip[(s, m)]["laeDb"] - neu[m]["laeDb"]) / 20)))}</td></tr>' for m in MOTIFS)
    return f'<section aria-labelledby="h-{s}"><h2 id="h-{s}">{s.capitalize()} <span class="sub">{e(V[s]["object"])} · unchanged since v3</span></h2><p class="caption">{e(V[s]["caption"])}</p><table><caption class="sr">{s}: voice beside the neutral fallback</caption><thead><tr><th scope="col">Notification</th><th scope="col">Voice</th><th scope="col">Neutral fallback</th></tr></thead><tbody>{rows}</tbody></table></section>'
page = f'''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Ratatoskr dings: v4 changes (unheard)</title>
<style>
:root{{color-scheme:light dark;--fg:#1b1f23;--bg:#fbfbf8;--mut:#4a5159;--line:#c9ccd0;--acc:#0b5cad}}
@media (prefers-color-scheme:dark){{:root{{--fg:#e8eaed;--bg:#14171a;--mut:#aab0b8;--line:#3a4047;--acc:#7ab7ff}}}}
body{{margin:0 auto;max-width:60rem;padding:1rem;font:1.05rem/1.5 system-ui,sans-serif;color:var(--fg);background:var(--bg)}}
h1{{font-size:1.5rem}}h2{{font-size:1.2rem;margin-top:2rem}}h3{{font-size:1.05rem}}.sub{{font-size:.9rem;color:var(--mut);font-weight:400}}
.notice{{border:2px solid var(--acc);padding:.75rem 1rem;border-radius:.4rem}}.caption{{margin:.25rem 0 .75rem}}
table{{border-collapse:collapse;width:100%}}th,td{{border-top:1px solid var(--line);padding:.4rem .5rem;text-align:left;vertical-align:top}}
audio{{width:100%;min-width:10rem}}img{{max-width:100%;height:auto;width:48%}}figure{{margin:1rem 0}}a,summary{{color:var(--acc)}}:focus-visible{{outline:3px solid var(--acc);outline-offset:2px}}.sr{{position:absolute;left:-9999px}}label{{display:block;margin:.5rem 0}}
</style></head><body>
<header><h1>Ratatoskr dings: what changed after v3 (v4)</h1>
<p class="notice"><strong>Nobody has heard v4.</strong> Not Meta, not Nibu, not its author. The Void diagnosis comes from spectrograms and from renders with one suspect removed; that the tick is what was heard as a double hit is a hypothesis until someone listens. Nothing plays until you press play. The clips are quiet (at most {S["maxTruePeakDbTP"]} dBTP), so start with your volume low. Not installed into Ratatoskr.</p>
<p>Changed: Void (no tick; a note that something follows ends by the next onset) and Nibu (proposal 3 for all four events). The other six voices are the same renders as v3, sample for sample (hashes of the raw renders checked); the shared loudness target did not move ({S["targetLaeDb"]} dB in v3 and v4), so their Ogg files are fresh encodes of the same raw audio: decoded, they differ from v3's by at most 0.0028 of full scale (-51 dB), measured; the cause (encoder variance) was not investigated</p>
<p>Loudness: every clip is matched to one A-weighted exposure; across all {len(M["clips"])} sender clips and {len(M["proposals"])} proposal clips as decoded, the spread is {S["laeSpreadDb"]} dB (target {S["targetLaeDb"]} dB, limited by {S["limitingClip"]}); the loudest true peak is {S["maxTruePeakDbTP"]} dBTP.</p></header>
<main>
{void_section()}
{nibu_section()}
<h2>Unchanged voices</h2>
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
