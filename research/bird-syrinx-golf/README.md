# Bird Syrinx Golf

This sidequest uses open-licensed bird recordings as a reality check for the graph-native syrinx path. The goal is not to make species-accurate bird impersonations yet. The goal is to force the paired-source syrinx graph to meet clean calls, emit listenable WAVs, and expose which controls currently matter.

## Source Set

All references are Wikimedia Commons files, with Commons file pages used as the license/provenance anchor and Commons MP3 transcodes used for robust local decoding. The Korean National Institute of Biological Resources clips are KOGL Type 1 rather than Creative Commons; keep that attribution distinction visible.

| id | species | call | author | license | source |
| --- | --- | --- | --- | --- | --- |
| `common-iora-xc125847` | Common Iora (`Aegithina tiphia`) | song | Sudipto Roy | CC BY-SA 3.0 | <https://commons.wikimedia.org/wiki/File:Aegithina_tiphia_-_Common_Iora_XC125847.ogg> |
| `common-blackbird-1059970` | Common Blackbird (`Turdus merula`) | song | Diana Tudor | CC BY 4.0 | <https://commons.wikimedia.org/wiki/File:Common_Blackbird_song_(Turdus_merula).ogg> |
| `red-footed-falcon` | Red-footed Falcon (`Falco vespertinus`) | typical calls | Bubulcus | CC BY 3.0 | <https://commons.wikimedia.org/wiki/File:Falco_vespertinus.ogg> |
| `warbling-white-eye-ko` | Warbling White-eye (`Zosterops japonicus`) | call | National Institute of Biological Resources | KOGL Type 1 | <https://commons.wikimedia.org/wiki/File:%EB%8F%99%EB%B0%95%EC%83%88.ogg> |
| `eurasian-wren-ko` | Eurasian Wren (`Troglodytes troglodytes`) | song | National Institute of Biological Resources | KOGL Type 1 | <https://commons.wikimedia.org/wiki/File:%EA%B5%B4%EB%9A%9D%EC%83%88.ogg> |

## Tool

`tools/BirdSyrinxGolf` downloads the sources, extracts the loudest 1.2 second window, renders a small grid of graph-native Aqua syrinx candidates, and writes:

- `reference-clip.wav`
- `candidate-syrinx.wav`
- `candidate-syrinx.aqua`
- `source.json`
- `report.txt`
- run-level `summary.md`

The generated audio lives under `artifacts/bird-syrinx-golf/<timestamp>/`, which is intentionally ignored by git.

## Clean Reference Run

Run: `artifacts/bird-syrinx-golf/20260528T143401133`

| bird | score | logMelCosine | best coarse control |
| --- | ---: | ---: | --- |
| Common Iora | 0.2423 | 0.2356 | `freq=1050`, high beak opening |
| Common Blackbird | 0.2784 | 0.3223 | `freq=1050`, high beak opening |
| Red-footed Falcon | 0.2727 | 0.3004 | `freq=3000`, narrow labial opening |
| Warbling White-eye | 0.3102 | 0.3692 | `freq=3000`, narrow labial opening |
| Eurasian Wren | 0.2577 | 0.2081 | `freq=1050`, high beak opening |

## Interpretation

This proves the current graph source path can render paired-labium syrinx candidates against clean real bird audio. It does not prove the model is good. The search grid is deliberately coarse and mostly static; it cannot yet match call envelopes, syllable timing, pitch sweeps, side-to-side alternation, beak articulation, or turbulence bursts with enough authority.

The useful finding is that the syrinx controls now form an inspectable fitting surface. The next coherent step is not a larger brute-force grid. It is time-varying control curves over the same graph: pressure envelopes, labial tension sweeps, independent left/right gating, beak opening, and possibly tract morphology changes when the reference call has obvious articulatory motion.
