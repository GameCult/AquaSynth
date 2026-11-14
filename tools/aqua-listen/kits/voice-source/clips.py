#!/usr/bin/env python3
"""Turns the six voice-source renders into the kit's clip files and measures them.
  clips.py <out-root>   reads out/<label>/<label>.wav, writes clips/*.wav and measures.txt
ros_matched is ros multiplied by (saw rms / ros rms) over the whole clip; every other clip is a byte copy of a render, and a clip used in two pairs is
written once per pair under its own file name, so each pair is a unit the page can shuffle. Needs numpy."""
import sys, wave, shutil, pathlib
import numpy as np
sys.path.insert(0, str(pathlib.Path(__file__).parent))
from hnr import read, hnr

out = pathlib.Path(sys.argv[1]); clips = out / "clips"; clips.mkdir(exist_ok=True)
w = lambda n: out / "out" / n / f"{n}.wav"
x = {n: read(str(w(n)))[0] for n in ["ref", "ros", "w20", "h20", "h25", "h15"]}
rms = lambda a: float(np.sqrt(np.mean(a * a)))
gain = rms(x["ref"]) / rms(x["ros"])
with wave.open(str(w("ros"))) as src, wave.open(str(clips / "ros_matched.wav"), "wb") as dst:
    dst.setparams(src.getparams())
    dst.writeframes((np.clip(x["ros"] * gain, -1, 1) * 32767).astype("<i2").tobytes())
files = {"saw_a.wav": "ref", "ros.wav": "ros", "saw_b.wav": "ref", "clean.wav": "ref", "w20.wav": "w20", "w20_b.wav": "w20", "h20.wav": "h20", "h25.wav": "h25", "h15.wav": "h15"}
for f, n in files.items():
    shutil.copyfile(w(n), clips / f)
lines = [f"{'clip':14s} {'HNR dB':>7s} {'rms':>8s} {'peak':>7s}"]
for name in sorted(p.name for p in clips.glob("*.wav")):
    a, _ = read(str(clips / name))
    lines.append(f"{name:14s} {hnr(a, 48000)[0]:7.1f} {rms(a):8.4f} {np.abs(a).max():7.3f}")
lines.append(f"ros_matched gain {gain:.3f} ({20 * np.log10(gain):.2f} dB)")
(out / "measures.txt").write_text("\n".join(lines) + "\n")
print("\n".join(lines))
