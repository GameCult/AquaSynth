#!/usr/bin/env python3
"""A spectrogram and a waveform PNG for every clip of a kit, and (with --kit and --renders) the burst notes.

  python3 -I clip_images.py <clips-dir> <images-dir> [--kit kit.json --renders <out-root>]

Needs numpy and matplotlib (Agg); no scipy. Writes <images-dir>/<wav stem>.spec.png and .wave.png for each *.wav in <clips-dir> (sorted).
The images are deterministic: fixed figure size, dpi and STFT (Hann 512, hop 128, 0-8 kHz, -90..0 dB re the clip's loudest bin), no PNG
metadata, so two runs give identical bytes. Nothing in the pictures marks a time; the notes do, and they are written next to <images-dir>
(burst-notes.md and burst-notes.json) and are not read by build.mjs.

--renders is the out-root of a kit's render.sh (out/<w>/script.aqua and out/<w>/<w>.wav, outnb/<w>/<w>.wav). For each clip of --kit the
note gives, for each plosive gesture of the word's script: closure start (the gesture's start), release = start of the with-minus-without
(--no-burst) waveform difference above 1/20 of its peak in the gesture's window (the definition PlosiveBurstExistsOnlyAroundItsOwnRelease
uses), burst end = last such sample of the same burst, and the window to look at. A --no-burst clip says 'no burst expected'.
"""
import argparse, json, re, sys, wave
from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

WIN, HOP, FMAX = 512, 128, 8000.0
DB_LO = -90.0


def read_wav(path):
    with wave.open(str(path), "rb") as w:
        assert w.getsampwidth() == 2 and w.getnchannels() == 1, f"{path}: expected 16-bit mono"
        return np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float64) / 32768.0, w.getframerate()


def stft_db(x, sr):
    """dB re the loudest bin of the clip; rows are frequency 0..FMAX, columns are frames (Hann window, numpy.fft)."""
    x = np.concatenate([x, np.zeros(max(0, WIN - len(x)))])
    n = 1 + (len(x) - WIN) // HOP
    idx = np.arange(WIN)[None, :] + HOP * np.arange(n)[:, None]
    mag = np.abs(np.fft.rfft(x[idx] * np.hanning(WIN)[None, :], axis=1)).T
    f = np.fft.rfftfreq(WIN, 1.0 / sr)
    mag = mag[f <= FMAX]
    return 20 * np.log10(np.maximum(mag, 1e-12) / max(mag.max(), 1e-12)), f[f <= FMAX], (np.arange(n) * HOP + WIN / 2) / sr


def save(fig, path):
    fig.savefig(path, dpi=100, metadata={"Software": None})
    plt.close(fig)


def images(wav, out):
    x, sr = read_wav(wav)
    db, f, t = stft_db(x, sr)
    fig, ax = plt.subplots(figsize=(8, 3.2))
    im = ax.imshow(np.clip(db, DB_LO, 0), origin="lower", aspect="auto", cmap="magma", vmin=DB_LO, vmax=0,
                   extent=[t[0] - HOP / 2 / sr, t[-1] + HOP / 2 / sr, f[0], f[-1]])
    ax.set_xlabel("time (s)"); ax.set_ylabel("Hz"); ax.set_title(f"spectrogram, Hann {WIN} hop {HOP}, 0-8 kHz", fontsize=9)
    fig.colorbar(im, ax=ax, label="dB re clip peak")
    fig.tight_layout(); save(fig, out / f"{wav.stem}.spec.png")
    fig, ax = plt.subplots(figsize=(8, 2.4))
    ax.plot(np.arange(len(x)) / sr, x, lw=0.5, color="black")
    ax.set_ylim(-0.5, 0.5); ax.set_xlim(0, len(x) / sr); ax.set_xlabel("time (s)"); ax.set_ylabel("amplitude (full scale 1.0)")
    ax.set_title("waveform, fixed range +-0.5", fontsize=9); ax.grid(alpha=0.3)
    fig.tight_layout(); save(fig, out / f"{wav.stem}.wave.png")


PLOSIVE_IPA = set("pbtdkgɡcɟqɢʔ")


def plosives(script):
    """(start, dur) of every plosive phoneme_gesture in a render's script.aqua (WordRender writes ipa=, no descriptor)."""
    out = []
    for line in script.read_text(encoding="utf-8").splitlines():
        if line.startswith("phoneme_gesture"):
            kv = dict(p.split("=", 1) for p in line.split()[1:] if "=" in p)
            if kv.get("ipa") in PLOSIVE_IPA: out.append((float(kv["start"]), float(kv["dur"])))
    return out


def burst_note(root, stem):
    """Notes for one render stem: the with-minus-without difference of out/<stem> and outnb/<stem>, per plosive."""
    script = root / "out" / stem / "script.aqua"
    if not script.exists():
        return {"burst": "not measured: no render of this word under out/"}
    ps = plosives(script)
    if not ps:
        return {"burst": "none: the word has no plosive"}
    twin = root / "outnb" / stem / f"{stem}.wav"
    if not twin.exists():
        return {"burst": "not measured: no --no-burst twin render of this word"}
    a, sr = read_wav(root / "out" / stem / f"{stem}.wav")
    b, _ = read_wav(twin)
    n = min(len(a), len(b)); d = np.abs(a[:n] - b[:n])
    rows = []
    for start, dur in ps:
        lo, hi = int(start * sr), min(n, int((start + dur + 0.12) * sr))
        peak = d[lo:hi].max() if hi > lo else 0.0
        hot = np.flatnonzero(d[lo:hi] > peak / 20) + lo if peak > 0 else np.array([], dtype=int)
        if not len(hot):
            rows.append({"closure_start_s": start, "release_s": None, "burst_end_s": None, "look_s": None, "note": "no difference between the renders"})
            continue
        end = hot[0]
        for i in hot[1:]:  # the first burst: stop at a gap over 5 ms
            if i - end > 0.005 * sr: break
            end = i
        rel, fin = hot[0] / sr, end / sr
        rows.append({"closure_start_s": round(start, 4), "release_s": round(rel, 4), "burst_end_s": round(fin, 4),
                     "difference_peak": round(float(peak), 4), "look_s": [round(max(0.0, rel - 0.03), 4), round(fin + 0.05, 4)]})
    return {"burst": rows}


def render_of(root, clip):
    """The out/<w> render a clip file was copied from (byte-identical wav), or None: two clip ids can be one render."""
    data = clip.read_bytes() if clip.exists() else None
    for d in sorted((root / "out").glob("*/")):
        w = d / f"{d.name}.wav"
        if data is not None and w.exists() and w.read_bytes() == data: return d.name
    return None


def notes(kit, root, clips):
    out = []
    for c in kit["clips"]:
        stem = Path(c["file"]).stem
        if stem.endswith("_noburst"):
            out.append({"id": c["id"], "file": c["file"], "word": stem[:-8], "burst": "no burst expected"}); continue
        word = render_of(root, clips / c["file"]) or stem
        out.append({"id": c["id"], "file": c["file"], "word": word, **burst_note(root, word)})
    return out


def notes_md(rows):
    lines = ["# Burst notes", "", "Times in seconds from the clip start. Release = start of the with-minus-without difference above 1/20 of its peak. Look in the spectrogram at 2-8 kHz inside the window.", ""]
    lines += ["| clip id | file | closure start | release | burst end | look in |", "|---|---|---|---|---|---|"]
    for r in rows:
        if isinstance(r["burst"], str):
            lines.append(f"| {r['id']} | {r['file']} | {r['burst']} | | | |"); continue
        for p in r["burst"]:
            look = f"{p['look_s'][0]} to {p['look_s'][1]}" if p.get("look_s") else p.get("note", "")
            lines.append(f"| {r['id']} | {r['file']} | {p['closure_start_s']} | {p['release_s']} | {p['burst_end_s']} | {look} |")
    return "\n".join(lines) + "\n"


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("clips"); ap.add_argument("images"); ap.add_argument("--kit"); ap.add_argument("--renders")
    a = ap.parse_args()
    clips, out = Path(a.clips), Path(a.images)
    if bool(a.kit) != bool(a.renders): ap.error("--kit and --renders go together")
    wavs = sorted(clips.glob("*.wav"))
    if not wavs: sys.exit(f"no .wav files in {clips}")
    out.mkdir(parents=True, exist_ok=True)
    for w in wavs: images(w, out)
    print(f"{len(wavs)} clips, {2 * len(wavs)} images in {out}")
    if a.kit:
        rows = notes(json.loads(Path(a.kit).read_text(encoding="utf-8")), Path(a.renders), clips)
        (out.parent / "burst-notes.json").write_text(json.dumps(rows, indent=1) + "\n", encoding="utf-8")
        (out.parent / "burst-notes.md").write_text(notes_md(rows), encoding="utf-8")
        print(f"burst notes for {len(rows)} clips in {out.parent}")


if __name__ == "__main__":
    main()
