"""Tests for sound-guard: python3 -m unittest -v test_guard   (needs numpy, scipy, soundfile with Vorbis, and node for the generator).
Clips are rendered by the generator into a temp dir and mutated in memory; nothing binary is committed. Each mutant must fail the assertion named."""
import ast, io, json, os, shutil, subprocess, sys, tempfile, unittest
from pathlib import Path
import numpy as np
import scipy.signal as ss
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parent))
import guard as g

TH = g.load_thresholds()
SR = 48000
WANT = g.intended() if shutil.which("node") else None
BASE_PEAK = 10 ** (-9 / 20)   # finish.py places the loudest clip at -9 dBTP
SHIPPED_LAE = -31.86          # finish.py's target exposure for the current sender set (metrics.json targetLaeDb)


def ms(t): return int(round(t * SR / 1000))


class Fixture(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not shutil.which("node"):
            raise unittest.SkipTest("node missing: the generator cannot render")
        cls.tmp = tempfile.mkdtemp(prefix="sound-guard-test-")
        subprocess.run(["node", str(g.TOOLS / "dings-voices/src/build.mjs"), cls.tmp], check=True, capture_output=True)
        cls.raw = {(s, m): sf.read(os.path.join(cls.tmp, f"{s}-{m}.wav"))[0] for s in g.SENDERS for m in g.MOTIFS}

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp, ignore_errors=True)

    def clip(self, s, m, peak=None):
        """A raw clip at the level finish.py gives it (every clip at one A-weighted exposure), or at an explicit peak."""
        x = self.raw[(s, m)].astype(np.float64)
        return x * peak / np.max(np.abs(x)) if peak else x * 10 ** ((SHIPPED_LAE - g.lae(x)) / 20)

    @staticmethod
    def ogg(x):
        b = io.BytesIO(); sf.write(b, x.astype(np.float32), SR, format="OGG", subtype="VORBIS"); b.seek(0)
        return sf.read(b, dtype="float64")[0]

    def check(self, x, voice, motif, family="dings"):
        notes = WANT["dings"][motif] if family == "dings" else WANT["earcon"][motif]
        return g.check_clip(x, SR, notes, g.policy_for(family, voice, TH), TH)[0]

    def kinds(self, fails): return sorted({(f.assertion, f.kind) for f in fails})

    def assertFails(self, fails, assertion, kind=None):
        got = self.kinds(fails)
        self.assertTrue(any(a == assertion and (kind is None or k == kind) for a, k in got), f"expected {assertion}/{kind}, got {got}")


class Baselines(Fixture):
    def test_unmutated_clips_pass(self):
        for s, m in [("void", "dm"), ("void", "invite"), ("void", "mention"), ("libby", "dm"), ("kiko", "invite"), ("weksa", "message"), ("nibu", "invite"), ("heimdall", "dm")]:
            for x in (self.clip(s, m), self.ogg(self.clip(s, m))):
                self.assertEqual(self.check(x, s, m), [], f"{s}/{m}")


def knock(n, at_ms, rel_db, tone_peak, seed=7):
    """The v3 Void 'soft knock': noise through a broad band-pass around 1.8 kHz, 4 ms attack, 6 ms decay, 24 ms long (voices.mjs v3 noiseBurst).
    v3 level: 36.6 dB under the tone's peak (measured by differencing the v3 and v4 renders)."""
    r = np.random.default_rng(seed).uniform(-1, 1, ms(24))
    y = ss.sosfilt(ss.butter(2, [1000, 2800], "bandpass", fs=SR, output="sos"), r)
    k = np.arange(len(y)); y *= np.minimum(1, (k + 1) / ms(4)) * np.exp(-k / SR / 0.006)
    y *= tone_peak * 10 ** ((-36.6 + rel_db) / 20) / np.max(np.abs(y))
    out = np.zeros(n); i = ms(at_ms); out[i:i + len(y)] = y[:n - i]
    return out


class Mutants(Fixture):
    def test_a_v3_knock_at_three_levels(self):
        for m in ("dm", "mention"):
            base = self.clip("void", m)
            at = 95 if m == "dm" else 190
            for rel in (0, -12, -20):   # -22 is caught only on the loudest clip: see README
                x = self.ogg(base + knock(len(base), at, rel, np.max(np.abs(base))))
                with self.subTest(motif=m, rel_db=rel):
                    self.assertFails(self.check(x, "void", m), "onsets", "stray_hf")

    def test_b_clipped_tail(self):
        base = self.clip("void", "dm")
        x = base.copy(); i = ms(300)
        x[i:] = np.clip(x[i:] * 40, -1, 1)   # last stretch driven into full scale
        self.assertFails(self.check(x, "void", "dm"), "clipping", "clip")           # exact plateaus in a WAV
        self.assertFails(self.check(self.ogg(x), "void", "dm"), "true_peak")        # after Vorbis the plateau is overs / true peak, not equal samples
        self.assertFails(self.check(self.ogg(x), "void", "dm"), "clipping", "clip")

    def test_b2_limited_plateau_below_full_scale(self):
        x = np.clip(self.clip("void", "mention"), -0.6 * BASE_PEAK, 0.6 * BASE_PEAK)   # a limiter flat-top, nowhere near 0.99
        self.assertFails(self.check(x, "void", "mention"), "clipping", "flat_top")

    def test_c_click_and_step(self):
        base = self.clip("void", "dm")
        click = base.copy(); click[ms(150)] += 0.2
        self.assertFails(self.check(click, "void", "dm"), "click_step_dc")
        self.assertFails(self.check(self.ogg(click), "void", "dm"), "click_step_dc")
        step = base.copy(); step[ms(150):] += 0.03
        self.assertFails(self.check(step, "void", "dm"), "click_step_dc")
        self.assertFails(self.check(self.ogg(step), "void", "dm"), "click_step_dc")

    def test_d_abrupt_cut(self):
        base = self.clip("void", "mention")
        cut = base[:ms(150)]
        self.assertFails(self.check(cut, "void", "mention"), "tail_end", "abrupt_end")
        self.assertFails(self.check(self.ogg(cut), "void", "mention"), "tail_end", "abrupt_end")

    def test_e_dc_offset(self):
        base = self.clip("void", "dm") + 0.01
        self.assertFails(self.check(base, "void", "dm"), "click_step_dc", "dc")
        self.assertFails(self.check(self.ogg(base), "void", "dm"), "click_step_dc", "dc")

    def test_f_loudness_outlier(self):
        laes = {f"dings/{s}/{m}": g.lae(self.raw[(s, m)]) for s in g.SENDERS for m in g.MOTIFS}
        target = float(np.median(list(laes.values())))
        eq = {}   # finish.py equalisation, without the encode
        for k, v in laes.items(): eq[k] = target
        self.assertEqual(g.loudness_failures(eq, TH)[0], {})
        for db, expect in ((3.0, True), (1.0, True), (0.3, False)):
            m = dict(eq); m["dings/void/dm"] = target + db
            got = g.loudness_failures(m, TH)[0]
            self.assertEqual(list(got), ["dings/void/dm"] if expect else [], f"+{db} dB")
        m = dict(eq); m["dings/void/dm"] = target - 3.0          # and a quiet outlier
        self.assertEqual(list(g.loudness_failures(m, TH)[0]), ["dings/void/dm"])

    def test_g_second_hit_in_and_out_of_window(self):
        base = self.clip("void", "mention")
        self.assertEqual(self.check(self.ogg(base), "void", "mention"), [])
        def echo(shift):
            x = np.concatenate([base, np.zeros(ms(shift))]); x[ms(shift):] += base
            return self.ogg(x)
        self.assertFails(self.check(echo(15), "void", "mention"), "onsets", "double")   # a flam inside the note window
        self.assertFails(self.check(echo(45), "void", "mention"), "onsets", "stray")    # a second hit outside it

    def test_h_missing_note_and_late_note(self):
        base = self.clip("libby", "dm")
        gone = base.copy(); gone[ms(200):] = 0
        self.assertFails(self.check(self.ogg(gone), "libby", "dm"), "onsets", "missing")
        late = np.concatenate([base[:ms(200)], np.zeros(ms(12)), base[ms(200):]])
        self.assertFails(self.check(self.ogg(late), "libby", "dm"), "onsets", "timing")

    def test_i_tail_rings_over_next_note(self):
        a = self.clip("libby", "mention")
        x = a.copy(); x[ms(240):] += a[:len(a) - ms(240)]
        self.assertFails(self.check(self.ogg(x), "libby", "dm"), "tail_overlap")
        # a ring-over voice (Aqua) is allowed to do it
        self.assertNotIn("tail_overlap", [f.assertion for f in self.check(self.ogg(x), "aqua", "dm")])

    def test_j_true_peak_ceiling(self):
        hot = self.clip("void", "mention", peak=10 ** (-3 / 20))
        self.assertFails(self.check(hot, "void", "mention"), "true_peak")

    def test_k_earcon_ceiling_is_its_own(self):
        x = self.clip("void", "mention", peak=10 ** (-1.5 / 20))   # over -2.5 dBTP, under the Dings -1 convention
        f = g.check_clip(x, SR, [0], g.policy_for("earcon", "warm-bell", TH), TH)[0]
        self.assertFails(f, "true_peak")
        ok = g.check_clip(self.clip("void", "mention", peak=10 ** (-3.2 / 20)), SR, [0], g.policy_for("earcon", "warm-bell", TH), TH)[0]
        self.assertNotIn("true_peak", [i.assertion for i in ok])


class Contract(unittest.TestCase):
    def leaves(self, d, path=()):
        if isinstance(d, dict) and "value" in d:
            yield path, d
        elif isinstance(d, dict):
            for k, v in d.items():
                if isinstance(v, dict): yield from self.leaves(v, path + (k,))

    def test_every_threshold_has_a_reason(self):
        found = list(self.leaves(TH["thresholds"])) + list(self.leaves(TH["voice_policy"])) + list(self.leaves(TH["aqua_ear_pins"]))
        self.assertGreaterEqual(len(found), 20)
        for path, leaf in found:
            self.assertTrue(isinstance(leaf.get("why"), str) and len(leaf["why"].split()) >= 8, f"{path} needs a real `why`")

    def test_every_threshold_the_code_reads_is_in_the_file(self):
        src = (Path(g.__file__)).read_text()
        names = set(__import__("re").findall(r'\b(?:tv|pol)\(th, "([a-z_]+)"\)', src))
        have = set(TH["thresholds"]) | set(TH["voice_policy"])
        self.assertEqual(names - have, set())
        self.assertEqual({n for n in TH["thresholds"]} - names, set(), "a threshold nothing reads")

    def test_aqua_ear_pins_hold_and_a_moved_pin_is_refused(self):
        g.check_pins(TH)
        bad = json.loads(json.dumps(TH)); bad["aqua_ear_pins"]["dc_abs"]["value"] = 0.05
        with self.assertRaises(g.GuardError): g.check_pins(bad)

    def test_known_failures_are_complete_and_stale_ones_are_loud(self):
        for k in TH["known_failures"]:
            self.assertTrue({"clip", "assertion", "value", "slack", "reason", "measured"} <= set(k), k)
            self.assertGreaterEqual(len(k["reason"].split()), 8)
        fake = {"known_failures": [{"clip": "c", "assertion": "loudness", "value": 5.0, "slack": 0.1, "reason": "r", "measured": "m"}]}
        passing = {"c": dict(fails=[], facts={})}
        failing, known, stale = g.apply_known(passing, fake)
        self.assertEqual((failing, known, len(stale)), ({}, [], 1))                       # started passing: reported
        moved = {"c": dict(fails=[g.Fail("loudness", "outlier", 9.0, "x")], facts={})}
        self.assertIn("c", g.apply_known(moved, fake)[0])                                  # got worse: fails until re-justified
        same = {"c": dict(fails=[g.Fail("loudness", "outlier", 5.05, "x")], facts={})}
        self.assertEqual(g.apply_known(same, fake)[0], {})                                 # still as recorded: printed, not failing

    def test_guard_source_has_no_magic_numbers(self):
        """Numeric literals other than 0, 1, 2 may only be module constants (UPPER_CASE = ...). Everything else is read from thresholds.json."""
        tree = ast.parse(Path(g.__file__).read_text())
        allowed = set()
        for n in tree.body:
            if isinstance(n, ast.Assign) and all(isinstance(t, ast.Name) and t.id.isupper() for t in n.targets):
                allowed |= {id(c) for c in ast.walk(n.value)}
        bad = [(c.value, c.lineno) for c in ast.walk(tree) if isinstance(c, ast.Constant) and isinstance(c.value, (int, float)) and not isinstance(c.value, bool)
               and c.value not in (0, 1, 2) and id(c) not in allowed]
        self.assertEqual(bad, [])


if __name__ == "__main__":
    unittest.main()
