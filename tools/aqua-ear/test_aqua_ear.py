"""Tests for aqua-ear on synthetic fixtures generated here (no committed binaries). Each detector has a case where it must fire and a
near-miss where it must not. Run: python -m unittest -v test_aqua_ear   (from this directory, with the README's venv)."""
import contextlib, io, json, os, shutil, struct, subprocess, tempfile, unittest
import numpy as np
import soundfile as sf
import aqua_ear as ae

SR = 48000
rng = np.random.default_rng(7)

def t_(sec): return np.arange(int(SR * sec)) / SR

def note(f=440.0, amp=0.5, dur=0.6, tau=0.15, attack=0.01, fade=0.03):
    """A decaying sine with a linear attack and (optionally) a closing fade to exact zero: the clean reference note."""
    t = t_(dur); x = amp * np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
    x *= np.minimum(1, t / attack)
    if fade: n = int(fade * SR); x[-n:] *= np.linspace(1, 0, n)
    return x

def burst(x, at_ms, amp=0.6, ms=3):
    n = int(ms * SR / 1000); i = int(at_ms * SR / 1000)
    x[i:i + n] += amp * np.sign(rng.standard_normal(n)) * np.exp(-np.arange(n) / (n / 4))
    return x

def rep_of(x, d, name):
    p = os.path.join(d, name + ".wav"); sf.write(p, x.astype(np.float32), SR, subtype="FLOAT")
    return ae.analyze(p)[0]

def fired(rep, name): return {d["name"]: d["fired"] for d in rep["detectors"]}[name]

def quiet(argv):
    with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()): return ae.main(argv)

class T(unittest.TestCase):
    @classmethod
    def setUpClass(c): c.d = tempfile.mkdtemp(prefix="aqua-ear-test-")
    @classmethod
    def tearDownClass(c): shutil.rmtree(c.d, ignore_errors=True)
    def rep(self, x, name="f"): return rep_of(x, self.d, name)

    # --- onsets
    def test_onset_times_within_2ms(self):
        x = np.zeros(int(SR * 0.5)); x[int(0.1 * SR):] += note(dur=0.4, attack=0.002, fade=0)[:len(x) - int(0.1 * SR)]
        r = self.rep(x, "on"); self.assertEqual(len(r["onsets"]), 1); self.assertAlmostEqual(r["onsets"][0]["ms"], 100, delta=2)
    def test_clean_note_all_clear(self):
        r = self.rep(note(), "clean")
        self.assertEqual(len(r["onsets"]), 1)
        self.assertEqual([d["name"] for d in r["detectors"] if d["fired"]], [])

    # --- double hit / flam
    def double(self, gap_ms): return burst(burst(note(amp=0.3, tau=0.4, dur=0.8), 100), 100 + gap_ms)
    def test_flam_fires_25ms(self):
        r = self.rep(self.double(25), "flam25"); self.assertTrue(fired(r, "double_hit"))
        h = [p for p in next(d for d in r["detectors"] if d["name"] == "double_hit")["evidence"] if p["one_note"]][0]
        self.assertAlmostEqual(h["gap_ms"], 25, delta=2)
    def test_flam_near_miss_150ms_apart(self):
        self.assertFalse(fired(self.rep(self.double(150), "flam150"), "double_hit"))
    def test_flam_near_miss_separate_staccato_notes(self):
        x = np.zeros(int(SR * 0.4))
        for at in (50, 90):  # 40 ms apart, each decays 60 dB in under 20 ms: two notes, not one
            n = note(dur=0.05, tau=0.003, attack=0.001, fade=0); i = int(at * SR / 1000); x[i:i + len(n)] += n
        self.assertFalse(fired(self.rep(x, "stac"), "double_hit"))
    def test_flam_near_miss_quiet_precursor(self):
        x = np.zeros(int(SR * 0.6))
        for at, amp, tau, dur in ((88, 0.01, 0.01, 0.02), (100, 0.3, 0.3, 0.5)):   # a -40 dBFS blip 12 ms before the real note: 30 dB apart, a precursor not a second hit
            n = note(amp=amp, tau=tau, dur=dur, attack=0.001 if dur < 0.1 else 0.004, fade=0); i = int(at * SR / 1000); x[i:i + len(n)] += n[:len(x) - i]
        r = self.rep(x, "pre"); d = next(d for d in r["detectors"] if d["name"] == "double_hit")
        self.assertFalse(d["fired"]); self.assertTrue(any(p["one_note"] and not p["comparable"] for p in d["evidence"]))   # it saw the pair and declined it
    def test_hf_transient_channel_finds_a_tick_the_full_band_envelope_misses(self):
        x = note(amp=0.3, tau=0.5, dur=0.8, f=300)
        for at in (200, 225): x = burst(x, at, amp=0.02, ms=2)     # clicks 20 dB under the tone: the broadband envelope barely moves
        r = self.rep(x, "tick"); self.assertEqual(len(r["onsets"]), 1)
        self.assertGreaterEqual(len(r["onsets_hf"]), 2); self.assertTrue(fired(r, "double_hit"))
    def test_hf_near_miss_ticks_150ms_apart(self):
        x = note(amp=0.3, tau=0.5, dur=0.8, f=300)
        for at in (200, 350): x = burst(x, at, amp=0.02, ms=2)
        r = self.rep(x, "tick150"); self.assertGreaterEqual(len(r["onsets_hf"]), 2); self.assertFalse(fired(r, "double_hit"))

    # --- click / step / DC
    def test_dc_fires_and_near_miss(self):
        self.assertTrue(fired(self.rep(note() + 0.05, "dc"), "click_step_dc"))
        self.assertFalse(fired(self.rep(note() + 0.001, "dc_small"), "click_step_dc"))
    def test_step_fires(self):
        x = note(dur=0.6, tau=1.0, fade=0); x[int(0.3 * SR):] += 0.3
        d = next(d for d in self.rep(x, "step")["detectors"] if d["name"] == "click_step_dc")
        ev = d["evidence"]["discontinuities"]; self.assertTrue(d["fired"]); self.assertEqual(ev[0]["kind"], "step"); self.assertAlmostEqual(ev[0]["ms"], 300, delta=1)
    def test_click_fires(self):
        x = note(); x[int(0.3 * SR)] += 0.6
        ev = next(d for d in self.rep(x, "click")["detectors"] if d["name"] == "click_step_dc")["evidence"]["discontinuities"]
        self.assertEqual(ev[0]["kind"], "click")
    def test_discontinuity_near_miss_bright_continuous_tone(self):
        x = 0.5 * np.sin(2 * np.pi * 8000 * t_(0.3)) * np.minimum(1, t_(0.3) / 0.01); x[-1440:] *= np.linspace(1, 0, 1440)
        self.assertFalse(fired(self.rep(x, "bright"), "click_step_dc"))

    # --- clipping / true peak
    def test_clipped_tone_fires(self):
        x = np.clip(1.5 * np.sin(2 * np.pi * 440 * t_(0.3)), -1, 1)
        d = next(d for d in self.rep(x, "clip")["detectors"] if d["name"] == "clipping_true_peak")
        self.assertTrue(d["fired"]); self.assertGreater(d["evidence"]["plateau_runs"], 0)
    def test_minus_1_dbfs_not_clipped(self):
        r = self.rep(0.891 * np.sin(2 * np.pi * 440 * t_(0.3)), "m1"); d = next(d for d in r["detectors"] if d["name"] == "clipping_true_peak")
        self.assertFalse(d["fired"]); self.assertAlmostEqual(d["evidence"]["sample_peak_dbfs"], -1.0, delta=0.05)
    def test_plateau_below_the_ceiling_is_not_clipping(self):
        x = np.clip(1.2 * np.sin(2 * np.pi * 440 * t_(0.3)), -0.8, 0.8)   # flat tops, but at -1.9 dBFS: a limiter shape, not a converter over
        d = next(d for d in self.rep(x, "lim")["detectors"] if d["name"] == "clipping_true_peak"); self.assertFalse(d["fired"])
    def test_intersample_over_fires_without_sample_over(self):
        x = 1.3 * np.sin(2 * np.pi * (SR / 4) * t_(0.1) + np.pi / 4)  # samples sit at +-0.919, the waveform between them reaches 1.3
        d = next(d for d in self.rep(x, "isp")["detectors"] if d["name"] == "clipping_true_peak")
        self.assertTrue(d["fired"]); self.assertEqual(d["evidence"]["over_samples"], 0); self.assertAlmostEqual(d["evidence"]["true_peak_dbtp"], 20 * np.log10(1.3), delta=0.3)

    # --- overlapping tails
    def pair(self, gap_ms, tau):
        x = np.zeros(int(SR * 1.2))
        for at in (100, 100 + gap_ms):
            n = note(dur=0.6, tau=tau, fade=0, attack=0.003); i = int(at * SR / 1000); x[i:i + len(n)] += n[:len(x) - i]
        return x
    def test_overlap_fires(self):
        self.assertTrue(fired(self.rep(self.pair(150, 0.2), "ovl"), "overlapping_tails"))
    def test_overlap_near_miss_short_tails(self):
        self.assertFalse(fired(self.rep(self.pair(300, 0.015), "novl"), "overlapping_tails"))

    # --- abrupt end
    def test_abrupt_end_fires(self):
        x = note(tau=1.0, fade=0, dur=0.3)  # still at about -9 dBFS when the file ends
        self.assertTrue(fired(self.rep(x, "cut"), "abrupt_end"))
    def test_abrupt_end_near_miss_faded_and_silent_tail(self):
        self.assertFalse(fired(self.rep(note(tau=1.0, fade=0.03, dur=0.3), "fade"), "abrupt_end"))
        x = np.concatenate([note(tau=1.0, fade=0, dur=0.3), np.zeros(4800)]); self.assertFalse(fired(self.rep(x, "sil"), "abrupt_end"))
        self.assertFalse(fired(self.rep(note(tau=0.05, fade=0, dur=0.6), "decayed"), "abrupt_end"))

    # --- ports from aqua-feel and I/O
    def test_loudness_and_true_peak_known_answers(self):
        r = self.rep(0.5 * np.sin(2 * np.pi * 440 * t_(2)), "lk")["info"]
        self.assertAlmostEqual(r["loudness_lufs"], -9.7, delta=0.2); self.assertAlmostEqual(r["sample_peak_dbfs"], -6.02, delta=0.05)
    def test_ports_match_aqua_feel_metrics_js(self):
        node = shutil.which("node"); js = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "aqua-feel", "metrics.js")
        if not node or not os.path.exists(js): self.skipTest("node or aqua-feel/metrics.js absent")
        x = (0.8 * np.sin(2 * np.pi * 997 * t_(1.0)) + 0.3 * rng.standard_normal(SR)).astype(np.float32)
        raw = os.path.join(self.d, "x.f32"); x.tofile(raw)
        code = f"import {{truePeak,integratedLoudness}} from 'file://{os.path.abspath(js)}';import fs from 'fs';const b=fs.readFileSync('{raw}');const x=new Float32Array(b.buffer,b.byteOffset,b.length/4);console.log(JSON.stringify([truePeak(x),integratedLoudness(x)]))"
        tp, lu = json.loads(subprocess.run([node, "--input-type=module", "-e", code], capture_output=True, text=True, check=True).stdout)
        mine_tp, _ = ae.true_peak(x.astype(np.float64), SR)
        self.assertAlmostEqual(mine_tp, tp, delta=1e-5); self.assertAlmostEqual(ae.loudness_lufs(x.astype(np.float64), SR), lu, delta=0.01)
    def test_formats_wav_ogg_flac(self):
        x = note()
        for ext in ("wav", "ogg", "flac"):
            p = os.path.join(self.d, "fmt." + ext); sf.write(p, x, SR)
            r = ae.analyze(p)[0]; self.assertEqual(len(r["onsets"]), 1, ext)

    # --- CLI and compare
    def test_cli_exit_codes_and_outputs(self):
        p = os.path.join(self.d, "dh.wav"); sf.write(p, self.double(25), SR); out = os.path.join(self.d, "o")
        self.assertEqual(quiet(["analyze", p, "--out", out]), 0)                 # findings never fail the run
        for f in ("spectrogram.png", "zoom.png", "envelope.png", "onsets.json", "onsets.txt", "report.json", "report.txt"): self.assertTrue(os.path.getsize(os.path.join(out, f)) > 0, f)
        self.assertEqual(quiet(["analyze", p, "--out", out, "--strict"]), 3)
        self.assertEqual(quiet(["analyze", os.path.join(self.d, "missing.wav"), "--out", out]), 2)
    def test_compare_reports_the_difference(self):
        a, b = os.path.join(self.d, "ca.wav"), os.path.join(self.d, "cb.wav"); sf.write(a, self.double(25), SR); sf.write(b, note(amp=0.3, tau=0.4, dur=0.8), SR)
        out = os.path.join(self.d, "cmp"); self.assertEqual(quiet(["compare", a, b, "--out", out]), 0)
        c = json.load(open(os.path.join(out, "compare.json")))
        self.assertTrue(next(d for d in c["detector_diff"] if d["name"] == "double_hit")["changed"]); self.assertTrue(len(c["onsets_only_in_a"]) >= 2)
        self.assertTrue(os.path.getsize(os.path.join(out, "compare.png")) > 0)

if __name__ == "__main__": unittest.main()
