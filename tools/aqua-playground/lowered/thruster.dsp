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

// brittle hull: high-Q modes rattled by noise, louder with load; tanh on the rattle alone keeps ring-up spikes from setting the peak
mode(f, q) = no.noise : fi.resonbp(f, q, 1);
rattle = (mode(173, 20) * (0.3 + 0.7 * load) + 0.8 * mode(311, 18) + 0.6 * mode(467, 16) + 0.4 * mode(1130, 14)) * (0.1 + 0.9 * load ^ 2) * 3 : *(4) : ma.tanh : /(4);

mix = rumble * (0.5 + 0.5 * load) + rasp * (0.3 + 1.5 * load) + rattle;
// drive falls only gently with load so tanh stays out of saturation while high rpm still ends up louder than low rpm
// final 0.425 trim keeps peak under 0.4 at rpm 4800
process = mix * (0.4 + 0.6 * load) * (0.9 - 0.5 * load) : ma.tanh : *(0.425);
