# Vocal Source Grounding

This packet grounds the next cut to Aqua's graph tissue-valve source law. The
trigger was the Warbling White-eye audition: a low-drive, smooth single-labium
candidate finally sounded birdlike, but the route there was suspicious. The
question is whether low drive and harmonic suppression are legitimate source
controls or compensators that should instead fall out of morphology, pressure,
tissue loss, aperture geometry, and acoustic load.

Downloaded/captured source files live in `sources/`.

## Decision Pressure

The current graph valve lowering still gives `drive` too much authority:

- it scales reservoir pressure, so it changes energy input;
- it participates in oscillation gating, so it changes phonation threshold;
- it scales modal/displacement contribution into flow, so it changes brightness;
- it sits beside `pressure`, `damping`, `saturation`, `load_coupling`, and
  `rest_opening`, which already have clearer physical meanings.

That is not one organ. That is a desk drawer full of half-labeled teeth.

## Literature Anchors

### Myoelastic-Aerodynamic Ownership

Elemans et al. 2015 provide the cleanest cross-species authority map: bird
syrinxes and mammal larynxes both use the myoelastic-aerodynamic mechanism.
Airflow supplies energy; tissue elasticity/restoring forces, tissue-wave phase,
and aerodynamic asymmetry sustain oscillation. The paper explicitly ties
frequency and mode of oscillation to mechanical tissue properties plus
aerodynamic driving forces, not a standalone timbre-gain parameter.

Implementation implication: `pressure` or `reservoir_pressure` is the energy
input. `mass`, `stiffness`, `damping`, layered phase, aperture geometry, and
load decide what that energy becomes. A single `drive` knob should not also own
spectral aggression.

### Syrinx Models Use Pressure/Tension Paths, Not Timbre Rescues

Mindlin/Laje-style syrinx work models syllables as trajectories through
physiological parameter space, especially pressure and stiffness/tension. Their
review emphasizes that the syrinx itself can be a source of complex acoustical
behavior, and that smooth excursions in pressure/stiffness parameter space can
produce recognizable syllables. The same review highlights that source-tract and
source-source coupling can create subharmonics, period-doubling, and complex
periodic/aperiodic behavior.

Implementation implication: the higher-level controller can own motif gestures,
but the lower-level source law must expose meaningful physical coordinates.
Harmonic behavior should arise from nonlinear source dynamics and coupling
regimes, not from a spectral smoothing knob.

### Mechanical Syrinx Experiments Separate Bronchial Pressure And Tension

Elemans et al. 2009 modeled the syrinx as a collapsible-tube/starling-resistor
style mechanical system with separately controlled bronchial pressure and
membrane tension. The captured abstract reports high-frequency self-sustained
oscillations, tension-dependent frequency, mass-dependent lower frequency
limits, and strong coupling to distal tube resonance.

Implementation implication: source morphology needs separate controls for
tension/stiffness, mass, damping/tissue loss, and tract load. Pressure should
drive the system; distal tube/reflection should perturb it. A generic low-drive
value is not the correct way to make a small bird source less harsh.

### Two-Mass Vocal Fold Modeling Keeps Flow, Tissue, And Tract Separate

Ishizaka and Flanagan's classic two-mass model approximates the vocal folds as
two stiffness-coupled masses, uses Bernoulli flow through the glottis, and
represents the tract as a transmission line. It calculates glottal area, glottal
volume velocity, mouth pressure, and relationships among subglottal pressure,
cord tension, glottal area, and duty ratio.

Implementation implication: our layered valve law should move toward explicit
upper/lower masses and aperture/flow equations. It should not hide duty ratio,
open quotient, or source spectral tilt behind `drive`.

### Self-Oscillating Fold Reviews Point To Coupled Nonlinear Physics

The 2024 review of synthetic self-oscillating vocal fold models frames phonation
as coupled flow dynamics, tissue motion, and acoustics. It calls out turbulence,
flow separation, large deformation/strain, collision, acoustic coupling, and
radiated acoustics as difficult but central nonlinear phenomena. It also
emphasizes that stiffness and geometry are meaningful experimental controls.

Implementation implication: if the current valve needs harmonic suppression,
candidate owners are damping/tissue loss, collision stiffness/damping, aperture
geometry, flow separation/loss, and radiation/load. A musical-synth style drive
stage is the wrong default owner.

### Vocal-Tract Loss Work Makes Area-Dependent Flow Loss A First-Class Actor

Birkholz and Haesner 2024 propose unified viscous and kinetic pressure losses in
discrete tube vocal-system models, including the glottis. Their measurements
support area-dependent viscous losses and partial pressure recovery at
expansions; the losses affect formant bandwidths, airflow, and turbulent source
power.

Implementation implication: some of the "harmonic suppression" we wanted by
turning drive down should instead come from source/tract loss and radiation
loading. For small apertures and narrow passages, area-dependent loss is not a
post-EQ trick; it is pressure-flow physics.

### Faust Can Express The Clean Version

Faust has first-class tools for this lane: recursive state with `~`, filters,
fractional delays, bidirectional waveguide primitives, mode filters, tube
models, reed tables, and nonlinear functions. We do not need to keep a fake
drive authority because of Faust. The hard part is choosing a source law that is
simple enough to inspect and stable enough for realtime.

Implementation implication: the next cut can stay Faust-friendly if it is a
small set of named locals: reservoir/downstream pressure, pressure drop,
flow-resistance/loss, upper/lower displacement and velocity, aperture,
collision/contact, load pressure, volume velocity, and radiation flow.

## Authority Map For The Next Cut

Owner: `AcousticSourcePort` source morphology owns local valve dynamics;
`AcousticNetwork` owns incident pressure/load and radiation; gesture/controller
layers own time-varying physiological inputs.

Inputs:

- Controller/gesture: `pressure`, `opening`, `tension`, left/right activation.
- Source morphology: `mass`, `upper_mass`, `lower_mass`, `stiffness`,
  `upper_stiffness`, `lower_stiffness`, `coupling_stiffness`, `damping`,
  `tissue_loss`, `collision_stiffness`, `collision_damping`,
  `rest_opening`, `aperture_shape`, `flow_loss`.
- Graph load: incident/downstream pressure, tract impedance, tube losses,
  radiation opening/reflection/loss.

Outputs:

- Source volume velocity/flow injection.
- Optional debug locals for aperture, pressure drop, contact, tissue energy,
  load pressure, and radiated flow.

Derived state:

- Dominant pitch, harmonic richness, phonation threshold, breathiness,
  brightness, and biphonation are outcomes of the above. They are not owned by
  `freq=` or a generic `drive`.

Forbidden writers:

- No hidden fixed oscillator floor when stiffness exists.
- No direct displacement-as-audio lane except as a tiny diagnostic/legacy term.
- No "harmonic suppression" or "bird smoothness" scalar.
- No source-owned spectral EQ pretending to be morphology.

## Proposed Cut, Not Yet Implemented

1. Split `drive` into physical pieces:
   - keep `pressure`/`reservoir_pressure` as the energy input;
   - introduce or repurpose `flow_scale` only as a unit/calibration factor;
   - move threshold/ease-of-oscillation into pressure drop, aperture, damping,
     and load;
   - move spectral harshness into aperture/contact/flow-loss/radiation.

2. Add explicit morphology fields only if they name physics:
   - `tissue_loss` or `viscous_loss` for dissipative tissue/flow losses;
   - `aperture_shape` for the nonlinear mapping from displacement to area;
   - `flow_loss` or `flow_resistance` for area-dependent pressure-flow loss;
   - keep `collision_stiffness`/`collision_damping` as the contact owner.

3. Change the source law shape:
   - compute `pressure_drop = reservoir_pressure - downstream/load pressure`;
   - compute aperture from rest opening + controlled opening + displacement;
   - compute flow from a pressure-flow relation damped by aperture-dependent
     resistance, not from arbitrary modal gain into `tanh`;
   - use modal/two-mass tissue state to modulate aperture and flow, not as a
     parallel oscillator output;
   - let tract/radiation losses shape brightness before any post-source
     normalization.

4. Keep a compatibility path only as explicit legacy:
   - `drive` can remain parsed for now, but it should lower to `flow_scale` or a
     deprecated alias with warnings/tests, not retain secret timbre authority.

## Open Questions

- Do we want `flow_scale` exposed in DSL at all, or should it be a patch
  calibration constant hidden behind morphology presets?
- Should `aperture_shape` be a continuous scalar first, or should we jump
  directly to named aperture laws such as `slot`, `wedge`, `membrane`, and
  `reed`?
- How aggressive should the v1 area-dependent pressure-flow loss be before we
  have a calibration set for birds?
- Should one-mass law remain the default for realtime, with two-mass/body-cover
  reserved for richer source patches, or should syrinx defaults move directly to
  two-mass because MEAD phase asymmetry is central?

## Source Index

- `pmc-elemans-2015-universal-mechanisms.html` and
  `elemans-2015-universal-mechanisms.pdf`
- `pmc-new-perspectives-birdsong-physics.html`
- `wur-elemans-mechanical-model.html`
- `nokia-ishizaka-flanagan-two-mass.html`
- `pmc-self-oscillating-vocal-fold-review.html`
- `vocaltractlab-birkholz-2024-losses.pdf` and `.txt`
- `faust-physmodels-doc.html`, `faust-filters-doc.html`,
  `faust-noises-doc.html`
