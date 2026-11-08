#!/usr/bin/env python3
"""Frequency-domain formants of a published area function. Independent of the AquaSynth ladder (no time-domain scattering,
no Faust): a chain of lossy uniform tubes (ABCD matrices) between a high-impedance glottis and a piston-in-baffle radiation load.

  tube_formants.py CSV [--vowel V] [--length-cm L] [--c 350] [--fmax 5000]
CSV is docs/research/data/story2008-area-functions.csv. --length-cm rescales every vowel to one tract length (the ladder
runs one length for all vowels); default is each vowel's own VTL. Prints F1..F4 as peaks of |U_lips / U_glottis|.
"""
import sys, argparse
import numpy as np

RHO = 1.14e-3  # g/cm^3 as in VocalTractLab's dissertation constants (SI 1.14 kg/m^3)

def load(path):
    rows = [l.strip() for l in open(path) if l.strip() and not l.startswith('#')]
    head = rows[0].split(',')[1:]
    areas = {v: [] for v in head}
    extra = {}
    for r in rows[1:]:
        c = r.split(',')
        if c[0].isdigit():
            for v, x in zip(head, c[1:]): areas[v].append(float(x))
        else:
            extra[c[0]] = dict(zip(head, map(float, c[1:])))
    return head, {v: np.array(a) for v, a in areas.items()}, extra

def transfer(areas, section_cm, c, freqs, wall=0.0):
    """|U_lip/U_glottis| for tubes of the given areas (cm^2) and one section length (cm). cgs units."""
    out = np.zeros(len(freqs))
    for n, f in enumerate(freqs):
        w = 2 * np.pi * f
        # attenuation per cm: viscous/thermal ~ f^0.5 plus wall term, giving F1 bandwidths of order 50-100 Hz
        alpha = 4e-5 * np.sqrt(f) + wall + 1.5e-3
        k = w / c * (1 - 1j * alpha * c / w) if w > 0 else 0
        M = np.eye(2, dtype=complex)
        for A in areas:  # glottis -> lips; state (P, U)
            Z = RHO * c / A
            kl = k * section_cm
            M = M @ np.array([[np.cos(kl), 1j * Z * np.sin(kl)], [1j * np.sin(kl) / Z, np.cos(kl)]])
        A_l = areas[-1]
        a = np.sqrt(A_l / np.pi)
        ka = w / c * a
        # Levine-Schwinger-ish low-ka piston radiation impedance (Flanagan: R||L)
        Zr = (RHO * c / A_l) * (ka ** 2 / 2 + 1j * 8 * ka / (3 * np.pi)) if ka < 1.2 else (RHO * c / A_l) * (1 - 0j)
        # U_glottis from open-circuit glottis (high Z): P_g free -> use U_g = 1, solve P_l = Zr*U_l; (P,U)_glottis = M (P,U)_lip
        # lip: P_l = Zr*U_l. glottis U_g = M[1,0]*P_l + M[1,1]*U_l = U_l*(M10*Zr + M11)
        out[n] = 1 / abs(M[1, 0] * Zr + M[1, 1])
    return out

def peaks(freqs, mag, n=4):
    idx = [i for i in range(1, len(mag) - 1) if mag[i] > mag[i - 1] and mag[i] >= mag[i + 1]]
    res = []
    for i in idx:
        y0, y1, y2 = np.log(mag[i - 1]), np.log(mag[i]), np.log(mag[i + 1])
        d = 0.5 * (y0 - y2) / (y0 - 2 * y1 + y2)  # parabolic refinement
        res.append(freqs[i] + d * (freqs[1] - freqs[0]))
    return res[:n]

def main():
    p = argparse.ArgumentParser()
    p.add_argument('csv'); p.add_argument('--vowel'); p.add_argument('--length-cm', type=float)
    p.add_argument('--c', type=float, default=350.0); p.add_argument('--fmax', type=float, default=5000)
    a = p.parse_args()
    head, areas, extra = load(a.csv)
    freqs = np.arange(50, a.fmax, 5.0)
    for v in head:
        if a.vowel and v != a.vowel: continue
        A = areas[v]
        L = a.length_cm or extra['vtl_cm'][v]
        sec = L * 100 / 100 / len(A)  # cm per section
        sec = L / len(A)
        m = transfer(A, sec, a.c * 100, freqs)
        print(f"{v}  L={L:.2f}cm  " + "  ".join(f"F{i+1}={f:.0f}" for i, f in enumerate(peaks(freqs, m))))

if __name__ == '__main__':
    main()
