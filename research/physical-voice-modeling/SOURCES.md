# Physical Voice Modeling Sources

Downloaded or captured on 2026-05-26.

## Local Source Files

| File | Source | Notes | SHA-256 |
| --- | --- | --- | --- |
| `sources/faust-2.85.5-delays.lib` | Local Faust 2.85.5 install, `C:\Program Files\Faust\share\faust\delays.lib` | Fractional delay implementations: linear, Lagrange, Thiran/allpass, variable delay. | `3089afb6654e6a37105d910f8b58f5c3bc4384acd57c5644227d69cb62486a39` |
| `sources/faust-2.85.5-physmodels.lib` | Local Faust 2.85.5 install, `C:\Program Files\Faust\share\faust\physmodels.lib` | Physical modeling primitives: bidirectional chains, length conversion, waveguides, tubes, terminations, wind/string models. | `4eaa76825372db0f79d0863912b46b21be73607dc99ac63dca3c` |
| `sources/mullen-howard-murphy-2006-waveguide-vocal-tract.pdf` | White Rose Research Online, `https://eprints.whiterose.ac.uk/id/document/715711` | Open PDF: waveguide vocal tract acoustics and increased-dimensionality discussion. | `cdb98482ea5dd2ac153e5097caa743f3691de0ddb747b0ae40466c2932ea106b` |
| `sources/mathur-story-rodriguez-2006-fractional-elongation.html` | Arizona Board of Regents page, `https://experts.azregents.edu/en/publications/vocal-tract-modeling-fractional-elongation-of-segment-lengths-in-/` | Metadata/abstract page for half-sample vocal-tract model with fractional-delay segment elongation. | `5ff7d56bb888b7dc7c7f402d38c51c31552edd0345ee7d5a972e9a784e6b236c` |
| `sources/frontiers-2023-ddsp-review.html` | Frontiers, `https://www.frontiersin.org/journals/signal-processing/articles/10.3389/frsip.2023.1284100/full` | Review article covering differentiable DSP for music/speech, including differentiable source-filter and Pink Trombone-style articulatory estimation. | `6f4f8cdfb5741df8a5e5be41304914cdbe887a8d5345161a8a63b8842fa54678` |

## Sources Not Fully Downloaded

- Mathur, Story, Rodriguez, "Vocal-tract modeling: Fractional elongation of
  segment lengths in a waveguide model with half-sample delays", IEEE TASLP
  2006, DOI `10.1109/TSA.2005.858550`.
  The local file is the accessible metadata/abstract page, not the full IEEE
  article PDF.

## Key Local Search Anchors

Useful terms inside the downloaded Faust libraries:

- `pm.l2s`
- `pm.chain`
- `pm.waveguideUd`
- `pm.waveguideFd`
- `pm.waveguideFd2`
- `pm.waveguideFd4`
- `de.fdelay`
- `de.fdelayltv`
- `de.fdelay1a`
- `de.fdelay2a`
- `de.fdelay3a`
- `de.fdelay4a`

