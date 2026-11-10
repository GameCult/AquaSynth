#!/usr/bin/env python3
"""sound-guard: one command that renders (or is given) every shipped Dings clip and every Earcon Workshop clip and checks each one with
tools/aqua-ear. Exit 0 = every assertion holds (known failures aside), 1 = something failed or a known failure went stale, 2 = tool error.
It measures signal facts. It cannot judge annoyance, timbre or feel. See README.md. Every number it compares against is in thresholds.json."""
import argparse, json, os, shutil, subprocess, sys, tempfile
sys.dont_write_bytecode = True   # the guard imports from sibling tools; it must not leave __pycache__ in their trees
from pathlib import Path

HERE = Path(__file__).resolve().parent
TOOLS = HERE.parent
sys.path.insert(0, str(TOOLS / "aqua-ear"))
sys.path.insert(0, str(TOOLS / "dings-voices" / "src"))
import numpy as np
import scipy.signal as ss
from scipy.ndimage import maximum_filter1d
import aqua_ear as ae
from metriclib import lae

MS_PER_S = 1000
SHIPPED_RATE = 48000     # Hz: every shipped clip (finish.py asserts it too)
NODE_TIMEOUT_S = 60
RENDER_TIMEOUT_S = 240
STDERR_TAIL = 800
HF_ORDER = 4            # Butterworth order of aqua-ear's high-band channel (aqua_ear.analyze); repeated here because the guard holds the envelope before detecting
SILENT_PEAK = 1e-6       # a clip whose peak is below this is silent: no onsets to look for
SENDERS = ["aqua", "eureka", "heimdall", "kiko", "libby", "nibu", "void", "weksa"]
MOTIFS = ["mention", "dm", "message", "invite"]


class GuardError(Exception):
    """Tool error (exit 2): the guard could not do its job, which is different from a clip failing."""


def load_thresholds(path=None):
    d = json.loads(Path(path or HERE / "thresholds.json").read_text())
    return d


def tv(th, name):
    return th["thresholds"][name]["value"]


def pol(th, name):
    return th["voice_policy"][name]["value"]


def check_pins(th):
    """aqua-ear owns its detector boundaries; the guard pins the ones it relies on so a loosening there cannot loosen the guard silently."""
    bad = [f"{k}: aqua_ear.TH={ae.TH.get(k)!r}, pinned {p['value']!r}" for k, p in th["aqua_ear_pins"].items() if ae.TH.get(k) != p["value"]]
    if bad:
        raise GuardError("aqua-ear thresholds differ from tools/sound-guard/thresholds.json aqua_ear_pins (re-justify the pin or revert aqua-ear): " + "; ".join(bad))


# ---- one failure --------------------------------------------------------------------------------------------------------
class Fail:
    def __init__(self, assertion, kind, value, msg):
        self.assertion, self.kind, self.value, self.msg = assertion, kind, float(value), msg

    def __repr__(self):
        return f"{self.assertion}/{self.kind}={self.value:g}: {self.msg}"


# ---- measurement ----------------------------------------------------------------------------------------------------------
def hold(env, n):
    """Peak-hold over the last n ms of a 1 ms envelope: rising edges are untouched, valleys shorter than n are filled."""
    return maximum_filter1d(env, size=n, origin=(n - 1) // 2)


def onset_rows(x, sr, th):
    """Onset rows (aqua-ear's detector and table) on the held full-band and held high-band envelopes of x preceded by pad_ms of silence.
    Times are in PADDED ms. Returns (rows, hf_rows, raw_env_full)."""
    pad, n = tv(th, "pad_ms"), tv(th, "hold_ms")
    xp = np.concatenate([np.zeros(int(sr * pad / MS_PER_S)), x])
    peak = float(np.max(np.abs(x))) if len(x) else 0.0
    if peak <= SILENT_PEAK:
        return [], [], ae.envelope_1ms(xp, sr)
    raw = ae.envelope_1ms(xp, sr)
    h = hold(raw, n)
    rows = ae.onset_table(h, ae.detect_onsets(h, float(h.max())), "full")
    hf_rows = []
    sos = ss.butter(HF_ORDER, ae.TH["hf_cut_hz"], "highpass", fs=sr, output="sos")
    eh = hold(ae.envelope_1ms(ss.sosfiltfilt(sos, xp), sr), n)
    floor = tv(th, "hf_floor_dbfs")
    if eh.max() > floor:
        hf_rows = ae.onset_table(eh, ae.detect_onsets(eh, float(eh.max()), floor), "hf")
    return rows, hf_rows, raw


def check_onsets(rows, hf_rows, notes_ms, swell, th):
    """Assertion 1. Each intended note t owns the window [t - pre, t + post]. Full band (not for swell voices): every window has an onset;
    its loudest onset is the note and starts within main_timing_ms of t; an onset before it is a precursor (pre-echo) only if precursor_db under it;
    any onset after it in the window is a double hit; any onset outside every window is stray. High band: only strays outside every window."""
    pad, pre, post = tv(th, "pad_ms"), tv(th, "window_pre_ms"), tv(th, "window_post_ms")
    wins = [(t + pad - pre, t + pad + post, t + pad) for t in notes_ms]
    inside = lambda ms: any(lo <= ms <= hi for lo, hi, _ in wins)
    out, mains = [], []
    rel = lambda ms: ms - pad
    for lo, hi, t in wins:
        w = [r for r in rows if lo <= r["ms"] <= hi]
        if not w:
            if not swell:
                out.append(Fail("onsets", "missing", 1, f"no full-band onset for the note at {rel(t):g} ms (window {rel(lo):g}..{rel(hi):g} ms)"))
            continue
        main = max(w, key=lambda r: r["peak_dbfs"])
        mains.append(main)
        if swell:
            continue
        if abs(main["ms"] - t) > tv(th, "main_timing_ms"):
            out.append(Fail("onsets", "timing", abs(main["ms"] - t), f"note at {rel(t):g} ms: loudest onset at {rel(main['ms'])} ms, {main['ms'] - t:+g} ms off (limit {tv(th, 'main_timing_ms'):g})"))
        for r in w:
            if r is main:
                continue
            if r["ms"] > main["ms"]:
                out.append(Fail("onsets", "double", 1, f"second full-band onset at {rel(r['ms'])} ms, {r['ms'] - main['ms']} ms after the note at {rel(main['ms'])} ms ({r['peak_dbfs'] - main['peak_dbfs']:+.1f} dB re it)"))
            elif main["peak_dbfs"] - r["peak_dbfs"] < tv(th, "precursor_db"):
                out.append(Fail("onsets", "double", 1, f"onset at {rel(r['ms'])} ms before the note at {rel(main['ms'])} ms is only {main['peak_dbfs'] - r['peak_dbfs']:.1f} dB under it (precursor limit {tv(th, 'precursor_db'):g} dB)"))
    if not swell:
        for r in rows:
            if not inside(r["ms"]):
                out.append(Fail("onsets", "stray", 1, f"full-band onset at {rel(r['ms'])} ms ({r['peak_dbfs']} dBFS) is not within a note window"))
    for r in hf_rows:
        if not inside(r["ms"]):
            out.append(Fail("onsets", "stray_hf", 1, f"high-band onset at {rel(r['ms'])} ms ({r['peak_dbfs']} dBFS in the >2 kHz band) is not within a note window"))
    return out, mains


def flat_top_runs(x, th):
    peak = float(np.max(np.abs(x)))
    hi = np.abs(x) >= tv(th, "flat_top_rel") * peak
    eq = np.concatenate([[False], np.abs(np.diff(x)) < tv(th, "flat_top_eps")])
    ok = hi & eq
    runs = run = 0
    for i in range(len(x)):
        run = run + 1 if ok[i] else 0
        if run == ae.TH["clip_run"] - 1:
            runs += 1
    return runs


def check_clip(x, sr, notes_ms, policy, th):
    """All per-clip assertions. policy: dict(ceiling_dbtp, swell, settle). Returns (failures, facts)."""
    f, facts = [], {}
    rows, hf_rows, raw = onset_rows(x, sr, th)
    of, mains = check_onsets(rows, hf_rows, notes_ms, policy["swell"], th)
    f += of
    facts["onsets"] = [r["ms"] - tv(th, "pad_ms") for r in rows]
    facts["onsets_hf"] = [r["ms"] - tv(th, "pad_ms") for r in hf_rows]
    # 2. click / step / DC: aqua-ear's detector. Steps on the padded clip (so a non-zero first sample counts), DC on the clip itself.
    xp = np.concatenate([np.zeros(int(sr * tv(th, "pad_ms") / MS_PER_S)), x])
    steps = ae.det_click_dc(xp, sr)["evidence"]["discontinuities"]
    dc = ae.det_click_dc(x, sr)["evidence"]["dc"]
    for s in steps:
        f.append(Fail("click_step_dc", s["kind"], s["jump"], f"{s['kind']} at {s['ms'] - tv(th, 'pad_ms'):g} ms, jump {s['jump']}"))
    if abs(dc) >= ae.TH["dc_abs"]:
        f.append(Fail("click_step_dc", "dc", abs(dc), f"DC offset {dc:+.5f} (limit {ae.TH['dc_abs']})"))
    # 3 and 4. clipping and true peak
    c = ae.det_clip(x, sr)
    if c["fired"]:
        e = c["evidence"]
        f.append(Fail("clipping", "clip", e["over_samples"] + e["plateau_runs"], f"{e['over_samples']} sample(s) over 1.0, {e['plateau_runs']} plateau run(s) at >= {ae.TH['clip_level']}, true peak {e['true_peak_dbtp']:.2f} dBTP"))
    runs = flat_top_runs(x, th)
    if runs:
        f.append(Fail("clipping", "flat_top", runs, f"{runs} run(s) of {ae.TH['clip_run']}+ equal samples at >= {tv(th, 'flat_top_rel')} of the clip's own peak"))
    tp = c["evidence"]["true_peak_dbtp"]
    facts["true_peak_dbtp"] = round(tp, 2)
    if tp > policy["ceiling_dbtp"]:
        f.append(Fail("true_peak", "ceiling", tp, f"true peak {tp:.2f} dBTP over the {policy['ceiling_dbtp']:g} dBTP ceiling"))
    # 5. tail settled at the end; tail settled before the next note for voices built that way
    peak = float(np.max(np.abs(x)))
    end = ae.det_end(x, sr, peak)
    if end["fired"]:
        f.append(Fail("tail_end", "abrupt_end", end["evidence"]["drop_db"], end["verdict"].split("  [")[0]))
    if policy["settle"] and len(mains) > 1:
        ov = ae.det_overlap(raw, mains)
        if ov["fired"]:
            h = [e for e in ov["evidence"] if e["tail_db_re_prev_peak"] > ae.TH["overlap_tail_db"]]
            f.append(Fail("tail_overlap", "overlap", max(e["tail_db_re_prev_peak"] for e in h), ov["verdict"].split("  [")[0]))
    return f, facts


def loudness_failures(lae_by_clip, th, neutral=()):
    """Assertion 6. Median over the sender clips; every clip (neutral too) within loudness_dev_db of it."""
    sender = [v for k, v in lae_by_clip.items() if k not in neutral]
    med = float(np.median(sender))
    band = tv(th, "loudness_dev_db")
    out = {}
    for k, v in lae_by_clip.items():
        if abs(v - med) > band:
            out[k] = Fail("loudness", "outlier", abs(v - med), f"LAE {v:.2f} dB is {v - med:+.2f} dB from the sender median {med:.2f} dB (band +-{band:g})")
    return out, med


# ---- sources: where the clips and the intended note times come from ------------------------------------------------------------
def node_json(js):
    try:
        r = subprocess.run(["node", "--input-type=module", "-e", js], capture_output=True, text=True, timeout=NODE_TIMEOUT_S, check=True)
    except (OSError, subprocess.SubprocessError) as e:
        raise GuardError(f"node is needed for the intended note times (RHYTHM in voices.mjs, EVENTS in earcon.js): {e}")
    return json.loads(r.stdout)


def intended():
    js = (f'import {{RHYTHM}} from "{(TOOLS / "dings-voices/src/voices.mjs").as_posix()}";'
          f'import {{EVENTS, IDENTITIES}} from "{(TOOLS / "earcon-workshop/web/earcon.js").as_posix()}";'
          'console.log(JSON.stringify({dings:Object.fromEntries(Object.entries(RHYTHM).map(([k,v])=>[k,v.map(n=>n[0])])),'
          'earcon:Object.fromEntries(Object.entries(EVENTS).map(([k,v])=>[k,v.notes.map(n=>n[1])])),identities:Object.keys(IDENTITIES)}))')
    return node_json(js)


def read(path):
    x, sr, ch = ae.load(str(path))
    return x, sr


def dings_clips(d, th, want):
    """Yield (id, path, family, voice, notes_ms). d is the generator's out dir (manifest.json, senders/, neutral/)."""
    d = Path(d)
    mf = d / "manifest.json"
    if not mf.exists():
        raise GuardError(f"{mf} missing: --dings takes the out dir finish.py writes")
    man = json.loads(mf.read_text())
    out = []
    for s in SENDERS:
        for m in MOTIFS:
            rel = man.get("senders", {}).get(s, {}).get(m)
            out.append((f"dings/{s}/{m}", d / rel if rel else None, "dings", s, want["dings"][m]))
    nev = pol(th, "neutral_events")
    for m in MOTIFS:
        rel = man.get("neutral", {}).get(m)
        out.append((f"dings/neutral/{m}", d / rel if rel else None, "neutral", "neutral", want["earcon"][nev[m]]))
    return out


def earcon_clips(d, want):
    """d holds one pack per identity (<d>/<identity>/pack.json + wavs, as cli.mjs writes), or is itself one pack."""
    d = Path(d)
    packs = {p.parent.name: p.parent for p in d.glob("*/pack.json")} or ({d.name: d} if (d / "pack.json").exists() else {})
    out = []
    for ident in want["identities"]:
        pk = packs.get(ident)
        pack = json.loads((pk / "pack.json").read_text()) if pk else None
        tempo = pack["params"]["tempo"] if pack else 1
        for ev, times in want["earcon"].items():
            file = pack["events"].get(ev, {}).get("file") if pack else None
            out.append((f"earcon/{ident}/{ev}", pk / file if file else None, "earcon", ident, [t * tempo for t in times]))
    return out


def render_all(tmp, neutral_dir):
    tmp = Path(tmp)
    dv = TOOLS / "dings-voices" / "src"
    steps = [(["node", str(dv / "build.mjs"), str(tmp / "dings" / "raw")], None)]
    (tmp / "dings" / "out").mkdir(parents=True)
    nd = Path(neutral_dir)
    if not nd.is_dir():
        raise GuardError(f"neutral fallback clips not found at {nd} (finish.py needs out/neutral/*.ogg); pass --neutral DIR")
    shutil.copytree(nd, tmp / "dings" / "out" / "neutral")
    steps.append(([sys.executable, str(dv / "finish.py"), str(tmp / "dings" / "raw"), str(tmp / "dings" / "out")], None))
    for ident in intended()["identities"]:
        steps.append((["node", str(TOOLS / "earcon-workshop" / "cli.mjs"), ident, str(tmp / "earcon" / ident)], None))
    for cmd, _ in steps:
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=RENDER_TIMEOUT_S)
        if r.returncode:
            raise GuardError(f"render step failed: {' '.join(cmd[:2])}\n{r.stderr[-STDERR_TAIL:]}")
    return tmp / "dings" / "out", tmp / "earcon"


# ---- run -----------------------------------------------------------------------------------------------------------------------
def policy_for(family, voice, th):
    if family == "dings":
        return dict(ceiling_dbtp=tv(th, "ceiling_dings_dbtp"), swell=voice in pol(th, "swell_voices"), settle=voice in pol(th, "settle_before_next_voices"))
    return dict(ceiling_dbtp=tv(th, "ceiling_earcon_dbtp"), swell=False, settle=False)   # neutral and earcon


def run(clips, th):
    """clips: [(id, path, family, voice, notes_ms)]. Returns {id: dict(fails=[Fail], facts)} with loudness folded in."""
    res, laes = {}, {}
    for cid, path, fam, voice, notes in clips:
        if path is None or not Path(path).exists():
            res[cid] = dict(fails=[Fail("present", "missing", 1, f"clip file missing ({path})")], facts={})
            continue
        x, sr = read(path)
        if sr != SHIPPED_RATE:
            res[cid] = dict(fails=[Fail("present", "sample_rate", sr, f"sample rate {sr}, expected {SHIPPED_RATE}")], facts={})
            continue
        f, facts = check_clip(x, sr, notes, policy_for(fam, voice, th), th)
        facts["duration_ms"] = round(len(x) * MS_PER_S / sr, 1)
        res[cid] = dict(fails=f, facts=facts)
        if fam in ("dings", "neutral"):
            laes[cid] = lae(x)
            facts["lae_db"] = round(laes[cid], 2)
    if laes:
        lf, med = loudness_failures(laes, th, neutral={k for k in laes if k.startswith("dings/neutral/")})
        for k, fl in lf.items():
            res[k]["fails"].append(fl)
    return res


def apply_known(res, th):
    """Returns (failing, known_lines, stale_lines). A known failure is a (clip, assertion) with the measured value and slack recorded in thresholds.json;
    it is printed on every run. If it stops failing it is stale (reported, and the run fails until the entry is removed); if its value moves by more than
    slack the run fails until the entry is re-justified."""
    known = {(k["clip"], k["assertion"]): k for k in th.get("known_failures", [])}
    failing, known_lines, stale, seen = {}, [], [], set()
    for cid, r in res.items():
        for fl in r["fails"]:
            k = known.get((cid, fl.assertion))
            if k is None:
                failing.setdefault(cid, []).append(fl)
                continue
            seen.add((cid, fl.assertion))
            if abs(fl.value - k["value"]) > k["slack"]:
                failing.setdefault(cid, []).append(Fail(fl.assertion, "known_moved", fl.value, f"known failure moved: recorded {k['value']} +-{k['slack']}, now {fl.value:g} ({fl.msg})"))
            else:
                known_lines.append(f"KNOWN  {cid} [{fl.assertion}/{fl.kind}] {fl.msg}   -- {k['reason']}")
    for key, k in known.items():
        if key not in seen:
            stale.append(f"STALE  known failure {key[0]} [{key[1]}] no longer fails: remove it from thresholds.json known_failures")
    return failing, known_lines, stale


def main(argv=None):
    ap = argparse.ArgumentParser(description="Check every shipped Dings clip and every Earcon Workshop clip with aqua-ear. Exit 1 on any failed assertion. With no directory it renders both itself into a temp dir.")
    ap.add_argument("--dings", help="out dir of tools/dings-voices (manifest.json, senders/, neutral/)")
    ap.add_argument("--earcon", help="dir with one cli.mjs pack per identity (<dir>/<identity>/pack.json)")
    ap.add_argument("--neutral", help="neutral fallback dir used when rendering Dings (default: thresholds.json config.neutral_source)")
    ap.add_argument("--thresholds", help="thresholds file (default: next to guard.py)")
    ap.add_argument("--keep", action="store_true", help="keep the temp render dir and print it")
    a = ap.parse_args(argv)
    try:
        th = load_thresholds(a.thresholds)
        check_pins(th)
        want = intended()
        tmp = None
        dd, ed = a.dings, a.earcon
        if not dd or not ed:
            tmp = tempfile.mkdtemp(prefix="sound-guard-")
            print(f"rendering into {tmp} ...", flush=True)
            rd, re_ = render_all(tmp, a.neutral or th["config"]["neutral_source"]["value"])
            dd, ed = dd or rd, ed or re_
        clips = dings_clips(dd, th, want) + earcon_clips(ed, want)
        res = run(clips, th)
    except GuardError as e:
        print(f"sound-guard: error: {e}", file=sys.stderr)
        return 2
    failing, known_lines, stale = apply_known(res, th)
    for cid, r in res.items():
        tag = "FAIL " if cid in failing else ("known" if any(l.startswith(f"KNOWN  {cid} ") for l in known_lines) else "ok   ")
        print(f"{tag} {cid:34s} peak {r['facts'].get('true_peak_dbtp', '?')} dBTP  onsets {r['facts'].get('onsets', [])}")
    for cid, fl in failing.items():
        for x in fl:
            print(f"FAILED {cid} [{x.assertion}/{x.kind}] {x.msg}")
    for l in known_lines + stale:
        print(l)
    n = len(res)
    print(f"\n{n} clips, {len(failing)} failing, {len(known_lines)} known failure(s), {len(stale)} stale known failure(s)."
          f" This guard measures signal facts; it cannot judge annoyance, timbre or feel.")
    if a.keep and tmp:
        print(f"kept {tmp}")
    elif tmp:
        shutil.rmtree(tmp, ignore_errors=True)
    return 1 if failing or stale else 0


if __name__ == "__main__":
    sys.exit(main())
