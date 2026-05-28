# Bird Syrinx Golf

This sidequest uses Creative Commons bird recordings as a reality check for the graph-native syrinx path. The goal is not to make species-accurate bird impersonations yet. The goal is to force the paired-source syrinx graph to meet real calls, emit listenable WAVs, and expose which controls currently matter.

## Source Set

All references are Wikimedia Commons mirrors of xeno-canto recordings, with Commons file pages used as the license/provenance anchor and Commons MP3 transcodes used for robust local decoding.

| id | species | call | author | license | source |
| --- | --- | --- | --- | --- | --- |
| `common-iora-xc125847` | Common Iora (`Aegithina tiphia`) | song | Sudipto Roy | CC BY-SA 3.0 | <https://commons.wikimedia.org/wiki/File:Aegithina_tiphia_-_Common_Iora_XC125847.ogg> |
| `bohemian-waxwing-xc132884` | Bohemian Waxwing (`Bombycilla garrulus`) | flight call | Bushman | CC BY-SA 3.0 | <https://commons.wikimedia.org/wiki/File:Bombycilla_garrulus_-_Bohemian_Waxwing_XC132884.ogg> |
| `california-quail-xc109825` | California Quail (`Callipepla californica`) | natural calls | Jonathon Jongsma | CC BY-SA 3.0 | <https://commons.wikimedia.org/wiki/File:Callipepla_californica_-_California_Quail_-_XC109825.ogg> |
| `american-crow-xc115429` | American Crow (`Corvus brachyrhynchos`) | soft rattling calls | Jonathon Jongsma | CC BY-SA 3.0 | <https://commons.wikimedia.org/wiki/File:Corvus_brachyrhynchos_-_American_Crow_-_XC115429.ogg> |

## Tool

`tools/BirdSyrinxGolf` downloads the sources, extracts the loudest 1.2 second window, renders a small grid of graph-native Aqua syrinx candidates, and writes:

- `reference-clip.wav`
- `candidate-syrinx.wav`
- `candidate-syrinx.aqua`
- `source.json`
- `report.txt`
- run-level `summary.md`

The generated audio lives under `artifacts/bird-syrinx-golf/<timestamp>/`, which is intentionally ignored by git.

## First Run

Run: `artifacts/bird-syrinx-golf/20260528T141443104`

| bird | score | logMelCosine | best coarse control |
| --- | ---: | ---: | --- |
| Common Iora | 0.2423 | 0.2356 | `freq=1050`, high beak opening |
| Bohemian Waxwing | 0.2785 | 0.2778 | `freq=3000`, narrow labial opening |
| California Quail | 0.1888 | 0.3035 | `freq=760`, wider labial opening |
| American Crow | 0.2225 | 0.3654 | `freq=760`, wider labial opening |

## Interpretation

This proves the current graph source path can render paired-labium syrinx candidates against real CC bird audio. It does not prove the model is good. The search grid is deliberately coarse and mostly static; it cannot yet match call envelopes, syllable timing, pitch sweeps, side-to-side alternation, beak articulation, or turbulence bursts with enough authority.

The useful finding is that the syrinx controls now form an inspectable fitting surface. The next coherent step is not a larger brute-force grid. It is time-varying control curves over the same graph: pressure envelopes, labial tension sweeps, independent left/right gating, beak opening, and possibly tract morphology changes when the reference call has obvious articulatory motion.
