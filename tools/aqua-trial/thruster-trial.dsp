// thruster-trial: patches/thruster/thruster.dsp plus two controls for the listening trial.
// `mix` (0 or 1, default 0) selects the mix: 0 is the current thruster.dsp, 1 is the v5 mix as cut at c6561da, before
// aqua-feel's 2026-11-05 edit (tools/aqua-trial/thruster-v5-c6561da.dsp holds that file; test.mjs asserts mix=1 equals it).
// The edit changed three things, each blended by `mix`: the rumble low-pass opening at low load, the rasp idle floor and slope,
// and the rattle idle floor.
// `noise` (percent, default 100) scales the two noise-driven layers (rasp, rattle) and leaves the rumble (the tone) alone, so lowering it
// moves the mix tone-forward. At noise=100 it is the same signal as the selected mix's thruster file (test.mjs asserts this sample for sample).
// Compile as Faust source; it is not a .aqua script.
// Control: rpm (300..6000). Rotor tone = 28 Hz + rpm*0.025 (about 36..180 Hz); three blades chop the rasp at 3x that.
declare name "thruster_trial";
import("stdfaust.lib");

noise_amt = hslider("noise", 100, 0, 100, 1) / 100;
mix_v5 = hslider("mix", 0, 0, 1, 1);
rpm   = hslider("rpm", 1200, 300, 6000, 1) : si.smooth(ba.tau2pole(0.2));
load  = (rpm - 300) / 5700;
shaft = 28 + rpm * 0.025;

// effort: slow random wander of the shaft speed
wander = no.noise : fi.lowpass(2, 5) : *(0.35);
f0     = shaft * (1 + 0.04 * wander);

// low rumble: saw at shaft speed plus a square a sub-octave down, low-passed
// the low-pass opens up as load falls, so at low rpm the rotor tone reaches the 150-300 Hz range a small speaker can show
rumble = (os.sawtooth(f0) + 0.7 * os.square(f0 * 0.5)) : fi.lowpass(3, 60 + 2.5 * shaft + 140 * (1 - load) * (1 - mix_v5));

// blade pass: rasp noise chopped at 3x shaft, band tracks rpm
chop = 0.5 + 0.5 * os.osc(f0 * 3) ^ 2;
rasp = no.noise : fi.resonbp(700 + rpm * 0.6, 1.5, 1) : *(chop);

// brittle hull: high-Q modes rattled by noise, louder with load (a 1.5% floor at idle, so low rpm is tone-led); tanh on the rattle alone keeps ring-up spikes from setting the peak
rattle_floor = 0.015 + 0.085 * mix_v5;
mode(f, q) = no.noise : fi.resonbp(f, q, 1);
rattle = (mode(173, 20) * (0.3 + 0.7 * load) + 0.8 * mode(311, 18) + 0.6 * mode(467, 16) + 0.4 * mode(1130, 14)) * (rattle_floor + (1 - rattle_floor) * load ^ 2) * 3 : *(4) : ma.tanh : /(4);

mix = rumble * (0.5 + 0.5 * load) + noise_amt * (rasp * ((0.05 + 0.25 * mix_v5) + (1.75 - 0.25 * mix_v5) * load) + rattle);
// drive falls only gently with load so tanh stays out of saturation while high rpm still ends up louder than low rpm
// final 0.425 trim keeps peak under 0.4 at rpm 4800
process = mix * (0.4 + 0.6 * load) * (0.9 - 0.5 * load) : ma.tanh : *(0.425);
