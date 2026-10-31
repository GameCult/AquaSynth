// thruster: sustained, heavy, brittle-hull thruster with an RPM control.
// Compile as Faust source (AquaSynthPatchCompiler.CompileSource); it is not a .aqua script.
// Control: rpm (300..6000). Rotor tone = 28 Hz + rpm*0.025 (about 36..180 Hz); three blades chop the rasp at 3x that.
declare name "thruster";
import("stdfaust.lib");

rpm   = hslider("rpm", 1200, 300, 6000, 1) : si.smooth(ba.tau2pole(0.2));
load  = (rpm - 300) / 5700;
shaft = 28 + rpm * 0.025;

// effort: slow random wander of the shaft speed
wander = no.noise : fi.lowpass(2, 5) : *(0.35);
f0     = shaft * (1 + 0.04 * wander);

// low rumble: saw at shaft speed plus a square a sub-octave down, low-passed
rumble = (os.sawtooth(f0) + 0.7 * os.square(f0 * 0.5)) : fi.lowpass(3, 60 + 2.5 * shaft);

// blade pass: rasp noise chopped at 3x shaft, band tracks rpm
chop = 0.5 + 0.5 * os.osc(f0 * 3) ^ 2;
rasp = no.noise : fi.resonbp(700 + rpm * 0.6, 1.5, 1) : *(chop);

// brittle hull: high-Q modes rattled by noise, louder with load
mode(f, q) = no.noise : fi.resonbp(f, q, 1);
rattle = (mode(173, 40) * (0.3 + 0.7 * load) + 0.8 * mode(311, 35) + 0.6 * mode(467, 30) + 0.4 * mode(1130, 25)) * (0.1 + 0.9 * load ^ 2) * 3;

mix = rumble * (0.5 + 0.5 * load) + rasp * (0.3 + 1.5 * load) + rattle;
// drive falls with load so the tanh stage stays out of saturation at high rpm; rasp and hull modes carry the strain
process = mix * (0.4 + 0.6 * load) * max(0.1, 0.9 - 1.0 * load) : ma.tanh : *(0.5);
