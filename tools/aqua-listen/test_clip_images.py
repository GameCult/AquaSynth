"""python3 -I test_clip_images.py   (numpy + matplotlib; synthetic clips, no render needed)"""
import hashlib, json, sys, tempfile, unittest, wave
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
import clip_images as ci

SR, BURST_AT = 48000, 0.30


def write(path, x):
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype("<i2").tobytes())


def word(with_burst):
    t = np.arange(int(0.6 * SR)) / SR
    x = 0.2 * np.sin(2 * np.pi * 300 * t) * (np.abs(t - 0.3) > 0.05)  # a vowel with a gap round the release
    if with_burst:
        i = int(BURST_AT * SR); n = int(0.012 * SR)
        x[i:i + n] += np.random.default_rng(1).normal(0, 0.1, n) * np.hanning(n)
    return x


class Fixture(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp()); self.clips = self.tmp / "clips"; self.clips.mkdir()
        write(self.clips / "ba.wav", word(True)); write(self.clips / "ba_noburst.wav", word(False))
        self.root = self.tmp / "r"
        for sub, wb in (("out", True), ("outnb", False)):
            (self.root / sub / "ba").mkdir(parents=True); write(self.root / sub / "ba" / "ba.wav", word(wb))
        (self.root / "out" / "ba" / "script.aqua").write_text("phoneme_gesture name=ba_0_ɑ ipa=ɑ start=0 dur=0.2 intensity=1\nphoneme_gesture name=ba_1_b ipa=b start=0.2 dur=0.095 intensity=1\n")
        self.kit = self.tmp / "kit.json"
        self.kit.write_text(json.dumps({"clips": [{"id": "x-ba", "file": "ba.wav"}, {"id": "x-ba-nb", "file": "ba_noburst.wav"}]}))

    def run_tool(self, out, *extra):
        sys.argv = ["clip_images", str(self.clips), str(out), *extra]; ci.main()

    def test_images_are_deterministic_and_complete(self):
        a, b = self.tmp / "a" / "images", self.tmp / "b" / "images"
        self.run_tool(a); self.run_tool(b)
        names = sorted(p.name for p in a.iterdir())
        self.assertEqual(names, ["ba.spec.png", "ba.wave.png", "ba_noburst.spec.png", "ba_noburst.wave.png"])
        for n in names:
            self.assertEqual(hashlib.sha256((a / n).read_bytes()).hexdigest(), hashlib.sha256((b / n).read_bytes()).hexdigest(), n)

    def test_notes_find_the_release_and_the_twin_says_none(self):
        self.run_tool(self.tmp / "o" / "images", "--kit", str(self.kit), "--renders", str(self.root))
        rows = json.loads((self.tmp / "o" / "burst-notes.json").read_text())
        p = rows[0]["burst"][0]
        self.assertAlmostEqual(p["release_s"], BURST_AT, delta=0.003)
        self.assertGreater(p["burst_end_s"], BURST_AT + 0.004); self.assertLess(p["burst_end_s"], BURST_AT + 0.02)
        self.assertEqual(rows[1]["burst"], "no burst expected")
        self.assertIn("no burst expected", (self.tmp / "o" / "burst-notes.md").read_text())

    def test_burst_shows_in_the_spectrogram_at_the_noted_time_only(self):
        def hi_db(x, at):
            db, f, t = ci.stft_db(x, SR); col = int(np.argmin(np.abs(t - at)))
            lin = 10 ** (db[f > 2000, col] / 20); return 20 * np.log10(np.sqrt((lin ** 2).sum()) + 1e-12)
        w, nb = word(True), word(False)
        self.assertGreaterEqual(hi_db(w, BURST_AT + 0.006) - hi_db(nb, BURST_AT + 0.006), 10)
        self.assertLess(hi_db(w, BURST_AT + 0.106) - hi_db(nb, BURST_AT + 0.106), 10)  # a note 100 ms late would find nothing


if __name__ == "__main__":
    unittest.main()
