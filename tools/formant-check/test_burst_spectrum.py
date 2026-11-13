#!/usr/bin/env python3
"""burst_spectrum isolates the burst: the difference of a render and its --no-burst twin is zero outside the release, and the peak and
centroid it reports are those of the noise that was added there.   python3 tools/formant-check/test_burst_spectrum.py"""
import os, tempfile, unittest, wave
import numpy as np
import weak_spots as w

SR = 48000

def write(root, sub, name, x):
    d = os.path.join(root, sub, name); os.makedirs(d, exist_ok=True)
    with wave.open(os.path.join(d, name + ".wav"), "wb") as f:
        f.setnchannels(1); f.setsampwidth(2); f.setframerate(SR); f.writeframes((np.clip(x, -1, 1) * 32767).astype("<i2").tobytes())

class BurstSpectrum(unittest.TestCase):
    def pair(self, tone_hz, release=.24, seconds=.5):
        t = np.arange(int(seconds * SR)) / SR
        shared = .2 * np.sin(2 * np.pi * 140 * t)               # the voiced word both renders share
        burst = np.zeros_like(t); a = int(release * SR); n = int(.015 * SR)
        burst[a:a + n] = .1 * np.sin(2 * np.pi * tone_hz * t[:n]) * np.hanning(n)
        root = tempfile.mkdtemp(); write(root, "out", "w", shared + burst); write(root, "outnb", "w", shared)
        return root, release

    def test_the_isolated_burst_is_zero_outside_the_release_and_peaks_where_it_was_put(self):
        root, release = self.pair(5000)
        r = w.burst_spectrum(root, "w", release, 20)
        self.assertLess(r["outside_peak"], 1e-4)               # 16-bit rounding only
        self.assertAlmostEqual(r["peak_hz"], 5000, delta=400)
        self.assertAlmostEqual(r["centroid_hz"], 5000, delta=600)
        self.assertGreater(r["window_peak"], .05)

    def test_a_lower_burst_has_the_lower_peak(self):
        lo = w.burst_spectrum(*self.pair(2800)[:1], "w", .24, 20)
        hi = w.burst_spectrum(*self.pair(5200)[:1], "w", .24, 20)
        self.assertLess(lo["peak_hz"], hi["peak_hz"])

if __name__ == "__main__": unittest.main()
