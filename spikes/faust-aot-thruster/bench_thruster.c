/* Per-block callback time vs deadline. usage: bench_thruster [blocks] [rate] [frames] */
#include <stdio.h>
#include <stdlib.h>
#include <time.h>
#include "aqs_thruster.h"

static int cmp(const void* a, const void* b) { double x = *(const double*)a, y = *(const double*)b; return (x > y) - (x < y); }

int main(int argc, char** argv) {
    int blocks = argc > 1 ? atoi(argv[1]) : 20000;
    int rate = argc > 2 ? atoi(argv[2]) : 48000;
    int frames = argc > 3 ? atoi(argv[3]) : 512;
    aqs_thruster* t = aqs_thruster_create(rate);
    if (!t || blocks < 100) return 2;
    float* out = malloc(sizeof(float) * frames);
    double* ms = malloc(sizeof(double) * blocks);
    const int warm = 200;
    for (int i = 0; i < warm + blocks; ++i) {
        /* sweep rpm across its whole range so the smoother and filters keep moving */
        aqs_thruster_set_rpm(t, 300.f + 5700.f * (0.5f + 0.5f * (float)((i % 997) - 498) / 498.f));
        struct timespec a, b;
        clock_gettime(CLOCK_MONOTONIC, &a);
        aqs_thruster_process(t, out, frames);
        clock_gettime(CLOCK_MONOTONIC, &b);
        if (i >= warm) ms[i - warm] = (b.tv_sec - a.tv_sec) * 1e3 + (b.tv_nsec - a.tv_nsec) / 1e6;
    }
    double deadline = 1e3 * frames / rate, sum = 0;
    int over = 0;
    for (int i = 0; i < blocks; ++i) { sum += ms[i]; if (ms[i] > deadline) ++over; }
    qsort(ms, blocks, sizeof *ms, cmp);
    double med = ms[blocks / 2], p99 = ms[(int)(blocks * 0.99)], mx = ms[blocks - 1];
    printf("rate=%d frames=%d blocks=%d deadline_ms=%.4f\n", rate, frames, blocks, deadline);
    printf("median_ms=%.4f p99_ms=%.4f max_ms=%.4f mean_ms=%.4f\n", med, p99, mx, sum / blocks);
    printf("median_pct_deadline=%.2f p99_pct_deadline=%.2f max_pct_deadline=%.2f over_deadline=%d\n",
           100 * med / deadline, 100 * p99 / deadline, 100 * mx / deadline, over);
    free(out); free(ms); aqs_thruster_destroy(t);
    return 0; /* timing is reported, not gated: a shared host preempts */
}
