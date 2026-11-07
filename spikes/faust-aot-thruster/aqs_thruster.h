/* C ABI over the AOT-compiled thruster (patches/thruster/thruster.dsp).
 * The host never touches Faust types. rpm enters only through aqs_thruster_set_rpm,
 * which enforces the bounds declared in the .dsp (hslider min/max/init). */
#ifndef AQS_THRUSTER_H
#define AQS_THRUSTER_H
#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32)
#define AQS_API __declspec(dllexport)
#else
#define AQS_API __attribute__((visibility("default")))
#endif

typedef struct aqs_thruster aqs_thruster;

/* Result of a parameter write. The applied value is always inside [min,max]. */
enum {
    AQS_PARAM_OK = 0,        /* applied as given */
    AQS_PARAM_CLAMPED = 1,   /* out of range (including +-inf): applied the nearest bound */
    AQS_PARAM_NAN = 2        /* NaN: applied the declared default */
};

/* Largest frame count one process call accepts; larger requests are rejected, not truncated. */
#define AQS_MAX_BLOCK 16384

AQS_API aqs_thruster* aqs_thruster_create(int sample_rate); /* NULL on bad rate (<8000 or >384000) or OOM */
AQS_API void aqs_thruster_destroy(aqs_thruster*);

/* Declared bounds, read from the compiled patch. */
AQS_API float aqs_thruster_rpm_min(const aqs_thruster*);
AQS_API float aqs_thruster_rpm_max(const aqs_thruster*);
AQS_API float aqs_thruster_rpm_default(const aqs_thruster*);

/* Returns AQS_PARAM_*; negative on NULL handle. */
AQS_API int aqs_thruster_set_rpm(aqs_thruster*, float rpm);
/* Value currently applied (post-clamp). */
AQS_API float aqs_thruster_rpm(const aqs_thruster*);

/* Counters since create, for the host to report: out-of-range and NaN writes. */
AQS_API unsigned aqs_thruster_clamp_count(const aqs_thruster*);
AQS_API unsigned aqs_thruster_nan_count(const aqs_thruster*);

/* Writes `frames` mono samples. Returns frames written, or -1 on NULL handle/out,
 * frames < 0, or frames > AQS_MAX_BLOCK. No allocation, no locks. */
AQS_API int aqs_thruster_process(aqs_thruster*, float* out, int frames);

#ifdef __cplusplus
}
#endif
#endif
