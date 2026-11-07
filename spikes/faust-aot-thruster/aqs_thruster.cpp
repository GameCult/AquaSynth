#include "aqs_thruster.h"

#include <cmath>
#include <new>

#include <faust/dsp/dsp.h>
#include <faust/gui/APIUI.h>
#include <faust/gui/meta.h>
#include "thruster_dsp.h"

struct aqs_thruster {
    ThrusterDsp dsp;
    APIUI ui;
    float lo, hi, init, applied;
    unsigned clamps = 0, nans = 0;
};

extern "C" {

aqs_thruster* aqs_thruster_create(int sample_rate) {
    if (sample_rate < 8000 || sample_rate > 384000) return nullptr;
    aqs_thruster* t = new (std::nothrow) aqs_thruster();
    if (!t) return nullptr;
    t->dsp.init(sample_rate);
    t->dsp.buildUserInterface(&t->ui);
    t->lo = t->ui.getParamMin(0);
    t->hi = t->ui.getParamMax(0);
    t->init = t->ui.getParamInit(0);
    t->applied = t->init;
    return t;
}

void aqs_thruster_destroy(aqs_thruster* t) { delete t; }

float aqs_thruster_rpm_min(const aqs_thruster* t) { return t->lo; }
float aqs_thruster_rpm_max(const aqs_thruster* t) { return t->hi; }
float aqs_thruster_rpm_default(const aqs_thruster* t) { return t->init; }

int aqs_thruster_set_rpm(aqs_thruster* t, float rpm) {
    if (!t) return -1;
    int status = AQS_PARAM_OK;
    if (std::isnan(rpm)) {
        rpm = t->init;
        status = AQS_PARAM_NAN;
        ++t->nans;
    } else if (rpm < t->lo) {
        rpm = t->lo;
        status = AQS_PARAM_CLAMPED;
        ++t->clamps;
    } else if (rpm > t->hi) {
        rpm = t->hi;
        status = AQS_PARAM_CLAMPED;
        ++t->clamps;
    }
    t->applied = rpm;
    t->ui.setParamValue(0, rpm);
    return status;
}

float aqs_thruster_rpm(const aqs_thruster* t) { return t->applied; }
unsigned aqs_thruster_clamp_count(const aqs_thruster* t) { return t->clamps; }
unsigned aqs_thruster_nan_count(const aqs_thruster* t) { return t->nans; }

int aqs_thruster_process(aqs_thruster* t, float* out, int frames) {
    if (!t || !out || frames < 0 || frames > AQS_MAX_BLOCK) return -1;
    float* outs[1] = {out};
    t->dsp.compute(frames, nullptr, outs);
    return frames;
}

} // extern "C"
