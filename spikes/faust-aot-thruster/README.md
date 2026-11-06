# Faust AOT spike: thruster v5 as a native lib (Linux)

Spike for `docs/faust-unity-player-scoping.md` path (a). One patch, `patches/thruster/thruster.dsp`
(copied here as `thruster.dsp`; re-copy if the patch changes), compiled by the `faust` CLI to C++ and
wrapped behind a C ABI. Not wired into the solution or into Unity.

Limitation: AOT fixes the patch set at build time, so player-authored patches cannot run this way.

- `aqs_thruster.h`: the C ABI. `rpm` enters only via `aqs_thruster_set_rpm`: bounds are read from the
  compiled patch's `hslider` (300..6000, default 1200); out-of-range and +-inf clamp to the nearest bound
  (`AQS_PARAM_CLAMPED`), NaN applies the default (`AQS_PARAM_NAN`); both are counted and returned.
  `aqs_thruster_process` rejects NULL, negative or >`AQS_MAX_BLOCK` frames instead of truncating.
- `test_thruster.c`: plain-C behaviour test (bounds, clamp, NaN, inf, hostile values give finite bounded
  output, rpm actually changes the output, instances are deterministic). A flipped clamp bound fails it.
- `bench_thruster.c`: per-block `process` time at 48 kHz / 512 frames against the 10.667 ms deadline.
- `run.sh`: generate, build, test, bench. `FAUST`, `FAUST_PREFIX` (needs `include/faust`), `FAUST_LIBS`
  (needs `stdfaust.lib`), `BLOCKS`, `RATE`, `FRAMES` override defaults. Exit status is test status; timing
  is reported, not gated.

Measured 2026-11-06, Faust 2.90.20, AMD EPYC-Genoa, shared Linux host, 20000 blocks, 3 runs:
median 0.052 ms (0.49% of deadline), p99 0.10-0.16 ms (~1-1.5%), max 10.8-17.1 ms (4-5 blocks of 20000 over
the deadline). The max is a scheduler preemption on a shared host, not DSP cost (median and p99 are
two orders under the deadline); a quiet or realtime-priority host is needed to say anything about the max.

**Unverified:** Windows (MSVC build, `__declspec(dllexport)`), Unity P/Invoke, IL2CPP, the Unity audio
thread, any device. Nothing here ran inside Unity.
