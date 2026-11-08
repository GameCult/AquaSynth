#!/usr/bin/env python3
"""Frequency-domain formants of a published area function. Independent of the AquaSynth ladder (no time-domain scattering,
no Faust): a chain of lossy uniform tubes (ABCD matrices) between a high-impedance glottis and a piston-in-baffle radiation load.

  tube_formants.py CSV [--vowel V] [--length-cm L] [--c 350] [--fmax 5000]
CSV is docs/research/data/story2008-area-functions.csv. --length-cm rescales every vowel to one tract length (the ladder
runs one length for all vowels); default is each vowel's own VTL. Prints F1..F4 as peaks of |U_lips / U_glottis|.

  tube_formants.py CSV --nasal [--vowel V|mean] [--closure-section N|none] [--a-vp 0.56] [--port-section 22] [--ladder-closure-cm CM [--lip-cap-cm D]] [--length-cm 17.52]
Nasal mode: |U_nostril / U_glottis| of the oral tract (Story area function, `mean` = the cellwise geometric mean of the eleven) with the
velopharyngeal port at section --port-section of 44 (Story 2005 tier IV), port area --a-vp (cm^2) as the first nasal section, the 19-section
nasal table after it (Birkholz 2005 Table 2.3 after Dang & Honda 1994, 0.6 cm sections), the nostril as a piston-in-baffle load, and the oral
tract beyond the port either closed rigid at the end of section --closure-section (the oral stop of /m n/) or open at the lips (nasalised vowel).
Prints N1 (lowest peak), the local minima with their depth, and the oral side-branch quarter-wave prediction c / (4 L_branch).
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

def alpha_of(f, wall=0.0):
    # attenuation per cm: viscous/thermal ~ f^0.5 plus wall term, giving F1 bandwidths of order 50-100 Hz
    return 4e-5 * np.sqrt(f) + wall + 1.5e-3

def abcd(areas, section_cm, c, f, wall=0.0):
    """Chain matrix of lossy uniform tubes, (P, U)_in = M (P, U)_out, cgs."""
    w = 2 * np.pi * f
    alpha = alpha_of(f, wall)
    k = w / c * (1 - 1j * alpha * c / w) if w > 0 else 0
    M = np.eye(2, dtype=complex)
    for A in areas:
        Z = RHO * c / A
        kl = k * section_cm
        M = M @ np.array([[np.cos(kl), 1j * Z * np.sin(kl)], [1j * np.sin(kl) / Z, np.cos(kl)]])
    return M

def radiation(A_l, c, f):
    """Low-ka piston-in-baffle radiation impedance (Flanagan R||L), matched impedance above ka 1.2."""
    w = 2 * np.pi * f
    ka = w / c * np.sqrt(A_l / np.pi)
    return (RHO * c / A_l) * (ka ** 2 / 2 + 1j * 8 * ka / (3 * np.pi)) if ka < 1.2 else (RHO * c / A_l) * (1 - 0j)

def transfer(areas, section_cm, c, freqs, wall=0.0):
    """|U_lip/U_glottis| for tubes of the given areas (cm^2) and one section length (cm). cgs units."""
    out = np.zeros(len(freqs))
    for n, f in enumerate(freqs):
        M = abcd(areas, section_cm, c, f, wall)
        Zr = radiation(areas[-1], c, f)
        # U_glottis = 1 (open-circuit glottis); lip P_l = Zr*U_l; U_g = M10*P_l + M11*U_l = U_l*(M10*Zr + M11)
        out[n] = 1 / abs(M[1, 0] * Zr + M[1, 1])
    return out

# Nasal tract area function, cm^2, velopharyngeal end first (Birkholz 2005 Table 2.3 after Dang & Honda 1994), 0.6 cm sections.
NASAL_TABLE = [1.63, 2.07, 2.72, 3.59, 4.24, 3.26, 3.04, 3.04, 2.72, 2.5, 2.39, 2.39, 1.85, 0.76, 1.41, 1.74, 1.30, 1.74, 0.76]
NASAL_SECTION_CM = 0.6

def nasal_transfer(oral_areas, oral_section_cm, port_section, closure_section, a_vp, c, freqs):
    """(|U_nostril / U_glottis|, |oral branch admittance|). The transfer's zeros are the maxima of the second:
    P_j = 0 where the mouth side of the junction is a short circuit. The oral tract is split after `port_section` sections: sections before it are the pharynx side, sections after
    it the mouth side (closed rigid end after `closure_section` sections, or open at the lips when None). The nasal branch is the table with its
    first section replaced by the port area a_vp (the ladder's first nasal cell is the port)."""
    pharynx = oral_areas[:port_section]
    end = len(oral_areas) if closure_section is None else closure_section
    mouth = oral_areas[port_section:end]
    nasal = [a_vp] + NASAL_TABLE[1:]
    out = np.zeros(len(freqs))
    yo = np.zeros(len(freqs))
    for n, f in enumerate(freqs):
        Mp = abcd(pharynx, oral_section_cm, c, f)
        Mn = abcd(nasal, NASAL_SECTION_CM, c, f)
        Zn = radiation(NASAL_TABLE[-1], c, f)
        yn = (Mn[1, 0] * Zn + Mn[1, 1]) / (Mn[0, 0] * Zn + Mn[0, 1])   # nasal input admittance U/P
        Mm = abcd(mouth, oral_section_cm, c, f)
        if closure_section is None:
            Zm = radiation(oral_areas[-1], c, f)
            ym = (Mm[1, 0] * Zm + Mm[1, 1]) / (Mm[0, 0] * Zm + Mm[0, 1])
        else:
            ym = Mm[1, 0] / Mm[0, 0]                                  # U_out = 0
        yo[n] = abs(ym)
        # glottis: U_g = M10 P_j + M11 U_1 = 1 with U_1 = (yn + ym) P_j
        Pj = 1 / (Mp[1, 0] + Mp[1, 1] * (yn + ym))
        out[n] = abs(Pj / (Mn[0, 0] * Zn + Mn[0, 1]))              # U_nostril = P_j / (M00 Zn + M01)
    return out, yo

def extrema(freqs, mag):
    """Local maxima and minima of mag with parabolic refinement in log magnitude: [(f, dB, 'peak'|'dip')]."""
    res = []
    for i in range(1, len(mag) - 1):
        kind = 'peak' if mag[i] > mag[i - 1] and mag[i] >= mag[i + 1] else 'dip' if mag[i] < mag[i - 1] and mag[i] <= mag[i + 1] else None
        if kind is None: continue
        y0, y1, y2 = np.log(mag[i - 1]), np.log(mag[i]), np.log(mag[i + 1])
        d = 0.5 * (y0 - y2) / (y0 - 2 * y1 + y2)
        res.append((freqs[i] + d * (freqs[1] - freqs[0]), 20 * np.log10(mag[i]), kind))
    return res

def ladder_closure(A, centre_fraction, constriction_cm=0.12, width=0.54, lip_cap_cm=None):
    """The AquaSynth ladder's own consonant shape on a 44-section area function, so the tube chain can be run on the constricted tract the ladder
    is given (a Gaussian narrowing, not a rigid closed end): d' = min(d, c + max(0, d - c)(1 - w)), w = exp(-((5 p - 5 centre) / width)^2) with p the
    section centre as a fraction of the tract (5 = sections - 1 of the 6-diameter morphology, width in those index units); lip_cap_cm caps the last section's diameter."""
    n = len(A)
    out = np.array(A, dtype=float)
    for i in range(n):
        p = (i + 0.5) / n
        w = np.exp(-(((p - centre_fraction) * 5) / width) ** 2)
        d = np.sqrt(out[i])
        out[i] = min(d, constriction_cm + max(0.0, d - constriction_cm) * (1 - w)) ** 2
    if lip_cap_cm is not None:
        out[-1] = min(np.sqrt(out[-1]), lip_cap_cm) ** 2
    return out

def nasal_report(args, head, areas, extra):
    if args.vowel == 'mean':
        A = np.exp(np.mean([np.log(areas[v]) for v in head], axis=0))
    else:
        A = areas[args.vowel]
    L = args.length_cm or 17.52
    sec = L / len(A)
    if args.ladder_closure_cm is not None:   # centre of the Gaussian narrowing, cm from the glottis
        A = ladder_closure(A, args.ladder_closure_cm / L, constriction_cm=args.closure_diameter_cm, lip_cap_cm=args.lip_cap_cm)
    closure = None if args.closure_section in (None, 'none') else int(args.closure_section)
    freqs = np.arange(20, args.fmax, 2.0)
    m, yo = nasal_transfer(A, sec, args.port_section, closure, args.a_vp, args.c * 100, freqs)
    ex = extrema(freqs, m)
    peaks_ = [e for e in ex if e[2] == 'peak']
    # depth of a dip: below the smaller of its neighbouring peaks
    lines = []
    for j, e in enumerate(ex):
        if e[2] != 'dip': continue
        nb = [x[1] for x in (ex[j - 1] if j > 0 else None, ex[j + 1] if j + 1 < len(ex) else None) if x is not None and x[2] == 'peak']
        lines.append(f"dip {e[0]:.0f} Hz ({e[1] - min(nb):+.1f} dB vs lower neighbour peak)" if nb else f"dip {e[0]:.0f} Hz")
    zeros = [freqs[i] for i in range(1, len(yo) - 1) if yo[i] > yo[i - 1] and yo[i] >= yo[i + 1]]
    where = 'open mouth' if closure is None else f"closed after section {closure}"
    print(f"{args.vowel} port@{args.port_section}/{len(A)} A_vp={args.a_vp} L={L}cm {where}")
    print("  peaks: " + "  ".join(f"{e[0]:.0f}({e[1]:.0f}dB)" for e in peaks_[:5]))
    print("  local minima (the first is the first null): " + "; ".join(lines[:5]))
    print("  transfer zeros (oral branch short-circuit, maxima of |Y_oral|): " + "  ".join(f"{z:.0f}" for z in zeros[:4]))
    if closure is not None:
        Lb = (closure - args.port_section) * sec
        print(f"  oral side branch {Lb:.2f} cm: quarter-wave {args.c * 100 / (4 * Lb):.0f} Hz, three-quarter {3 * args.c * 100 / (4 * Lb):.0f} Hz")

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
    p.add_argument('--nasal', action='store_true'); p.add_argument('--closure-section', default=None)
    p.add_argument('--ladder-closure-cm', type=float); p.add_argument('--lip-cap-cm', type=float); p.add_argument('--closure-diameter-cm', type=float, default=0.12)
    p.add_argument('--a-vp', type=float, default=0.56); p.add_argument('--port-section', type=int, default=22)
    a = p.parse_args()
    head, areas, extra = load(a.csv)
    if a.nasal:
        nasal_report(a, head, areas, extra)
        return
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
