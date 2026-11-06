/* Behaviour test for the bounded-parameter boundary. Plain C: proves the ABI is callable without C++. */
#include <math.h>
#include <stdio.h>
#include <string.h>
#include "aqs_thruster.h"

static int fails;
#define CHECK(c) do { if (!(c)) { printf("FAIL line %d: %s\n", __LINE__, #c); ++fails; } } while (0)

static float peak_after(aqs_thruster* t, float rpm, int* finite) {
    static float buf[512];
    float pk = 0;
    *finite = 1;
    aqs_thruster_set_rpm(t, rpm);
    for (int b = 0; b < 200; ++b) { /* ~2 s: lets the 0.2 s rpm smoother settle */
        CHECK(aqs_thruster_process(t, buf, 512) == 512);
        for (int i = 0; i < 512; ++i) {
            if (!isfinite(buf[i])) *finite = 0;
            if (fabsf(buf[i]) > pk) pk = fabsf(buf[i]);
        }
    }
    return pk;
}

int main(void) {
    CHECK(aqs_thruster_create(100) == NULL);
    CHECK(aqs_thruster_create(1000000) == NULL);
    aqs_thruster* t = aqs_thruster_create(48000);
    CHECK(t != NULL);
    if (!t) return 1;

    /* bounds come from the .dsp hslider("rpm", 1200, 300, 6000, 1) */
    CHECK(aqs_thruster_rpm_min(t) == 300.f);
    CHECK(aqs_thruster_rpm_max(t) == 6000.f);
    CHECK(aqs_thruster_rpm_default(t) == 1200.f);
    CHECK(aqs_thruster_rpm(t) == 1200.f);

    CHECK(aqs_thruster_set_rpm(t, 4800.f) == AQS_PARAM_OK && aqs_thruster_rpm(t) == 4800.f);
    CHECK(aqs_thruster_set_rpm(t, 300.f) == AQS_PARAM_OK && aqs_thruster_rpm(t) == 300.f);
    CHECK(aqs_thruster_set_rpm(t, 6000.f) == AQS_PARAM_OK && aqs_thruster_rpm(t) == 6000.f);
    CHECK(aqs_thruster_clamp_count(t) == 0 && aqs_thruster_nan_count(t) == 0);

    CHECK(aqs_thruster_set_rpm(t, 299.9f) == AQS_PARAM_CLAMPED && aqs_thruster_rpm(t) == 300.f);
    CHECK(aqs_thruster_set_rpm(t, -50.f) == AQS_PARAM_CLAMPED && aqs_thruster_rpm(t) == 300.f);
    CHECK(aqs_thruster_set_rpm(t, 6000.1f) == AQS_PARAM_CLAMPED && aqs_thruster_rpm(t) == 6000.f);
    CHECK(aqs_thruster_set_rpm(t, INFINITY) == AQS_PARAM_CLAMPED && aqs_thruster_rpm(t) == 6000.f);
    CHECK(aqs_thruster_set_rpm(t, -INFINITY) == AQS_PARAM_CLAMPED && aqs_thruster_rpm(t) == 300.f);
    CHECK(aqs_thruster_clamp_count(t) == 5);

    aqs_thruster_set_rpm(t, 5000.f);
    CHECK(aqs_thruster_set_rpm(t, NAN) == AQS_PARAM_NAN && aqs_thruster_rpm(t) == 1200.f);
    CHECK(aqs_thruster_nan_count(t) == 1);

    CHECK(aqs_thruster_set_rpm(NULL, 1000.f) < 0);
    float buf[16];
    CHECK(aqs_thruster_process(t, buf, -1) == -1);
    CHECK(aqs_thruster_process(t, buf, AQS_MAX_BLOCK + 1) == -1);
    CHECK(aqs_thruster_process(t, NULL, 16) == -1);
    CHECK(aqs_thruster_process(NULL, buf, 16) == -1);
    CHECK(aqs_thruster_process(t, buf, 0) == 0);

    /* hostile inputs never reach the DSP: output stays finite and under the patch's own peak bound */
    float hostile[] = {NAN, INFINITY, -INFINITY, 1e30f, -1e30f, 0.f};
    for (unsigned i = 0; i < sizeof hostile / sizeof *hostile; ++i) {
        aqs_thruster* h = aqs_thruster_create(48000);
        int fin;
        float pk = peak_after(h, hostile[i], &fin);
        CHECK(fin);
        CHECK(pk > 0.01f && pk < 1.0f);
        aqs_thruster_destroy(h);
    }

    /* the patch is audibly rpm-dependent and loud at high rpm (guards against a stuck parameter) */
    int fin;
    aqs_thruster* lo = aqs_thruster_create(48000);
    aqs_thruster* hi = aqs_thruster_create(48000);
    float plo = peak_after(lo, 300.f, &fin), phi = peak_after(hi, 4800.f, &fin);
    CHECK(phi > plo);
    CHECK(phi < 0.45f); /* patch comment: peak under 0.4 at rpm 4800, small margin */
    aqs_thruster_destroy(lo); aqs_thruster_destroy(hi);

    /* same rpm, same sample rate: two instances render identical bytes (noise is seeded) */
    aqs_thruster* a = aqs_thruster_create(48000);
    aqs_thruster* b = aqs_thruster_create(48000);
    static float xa[2048], xb[2048];
    aqs_thruster_set_rpm(a, 2000.f); aqs_thruster_set_rpm(b, 2000.f);
    aqs_thruster_process(a, xa, 2048); aqs_thruster_process(b, xb, 2048);
    CHECK(memcmp(xa, xb, sizeof xa) == 0);
    aqs_thruster_destroy(a); aqs_thruster_destroy(b);

    aqs_thruster_destroy(t);
    printf(fails ? "FAILED (%d)\n" : "ok\n", fails);
    return fails != 0;
}
