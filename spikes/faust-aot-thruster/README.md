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
- `bench_thruster.c`: per-block `process` time at 48 kHz / 512 frames against the 10.667 ms deadline; prints one `OVERRUN` line
  per block over the deadline with wall-clock timestamp, wall ms and thread CPU ms.
- `run.sh`: generate, build, test, bench. `FAUST`, `FAUST_PREFIX` (needs `include/faust`), `FAUST_LIBS`
  (needs `stdfaust.lib`), `BLOCKS`, `RATE`, `FRAMES`, `CORE` (pin the bench to one cpu via `taskset`), `NOOP=1` (time a busy-wait
  instead of the patch) override defaults. Exit status is test status; timing
  is reported, not gated.

**Scope: this measures a bare C loop.** `bench_thruster` calls `aqs_thruster_process` back to back on one
Linux thread. It shows the patch is cheap, not that a Unity player meets its deadline (no Unity audio
thread, P/Invoke, IL2CPP, mixer or Windows scheduling in it).

Measured 2026-11-06/07, Faust 2.90.20, AMD EPYC-Genoa, shared Linux host (load average ~8 on 16 cores):
median 0.052 ms (0.49% of the 10.667 ms deadline), p99 0.08-0.16 ms (~1-1.5%). Cheap on this host:
median 0.052 ms, p99 under 0.16 ms. The host's tail is not judgeable (see below).

**Max block time: UNEXPLAINED. It is not a pass.** Max was 10.8-17.1 ms, with 1-5 of 20000 blocks over
the deadline per run (400000-block `process` runs: 70-74 over; see the control below). What the instrumented runs (`run.sh`, `CORE=<cpu>` pins with
`taskset`; every overrun prints a `wall_unix` timestamp, wall ms and thread CPU ms) do show:

- Not per-block DSP cost: `NOOP=1` times a 0.05 ms busy-wait instead of `process`, and stalls about the
  same way (core 3, 400000 blocks: 74 overruns with `process`, 67 with the busy-wait, both ~11 ms; counts
  differ by ~10%, no run-to-run spread measured for the control).
- Not a DSP-state pattern: overrun block indices differ every run and are not tied to the rpm sweep.
- Periodic in wall time, per core: pinned to core 3, overruns recur every ~0.3-0.4 s (mostly); pinned
  to core 9, every ~1-2 s (17 in 400000 blocks). Cause of the periodicity untested (not shown to be a timer). Unpinned runs get 1-5 per 20000.
- The thread's CPU time over a stalled block is about equal to its wall time (e.g. 10.84 vs 10.85 ms),
  so the stall is charged to the thread rather than showing as time off-CPU. An ordinary guest
  run-queue preemption would be expected to show off-CPU time; this does not. The host (vCPU steal) is
  left open. The earlier claim that these were scheduler preemption is withdrawn, not refuted.

Cause not identified (candidates: hypervisor/container CPU accounting, per-core host timer or housekeeping
work, SMI; none tested). Whether the player would see it is unknown. A quiet, realtime-priority or
isolated-core host is needed before the max can be called anything.

**Unverified:** Windows (MSVC build, `__declspec(dllexport)`), Unity P/Invoke, IL2CPP, the Unity audio
thread, any device. Nothing here ran inside Unity.
