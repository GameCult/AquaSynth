/* ------------------------------------------------------------
name: "thruster"
Code generated with Faust 2.90.20 (https://faust.grame.fr)
Compilation options: -lang cpp -i -fpga-mem-th 4 -ct 1 -cn ThrusterDsp -es 1 -mcd 16 -mdd 1024 -mdy 33 -single -ftz 0
------------------------------------------------------------ */

#ifndef  __ThrusterDsp_H__
#define  __ThrusterDsp_H__

#ifndef FAUSTFLOAT
#define FAUSTFLOAT float
#endif 

/* link with : "" */
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <math.h>
#ifndef FAUST_INT_WRAP
#define FAUST_INT_WRAP
inline int faust_wrap_add(int a, int b) { return int((unsigned int)a + (unsigned int)b); }
inline int faust_wrap_sub(int a, int b) { return int((unsigned int)a - (unsigned int)b); }
inline int faust_wrap_mul(int a, int b) { return int((unsigned int)a * (unsigned int)b); }
#endif


#ifndef FAUSTCLASS 
#define FAUSTCLASS ThrusterDsp
#endif

#ifdef __APPLE__ 
#define exp10f __exp10f
#define exp10 __exp10
#endif

#if defined(_WIN32)
#define RESTRICT __restrict
#else
#define RESTRICT __restrict__
#endif

class ThrusterDspSIG0 {
	
  private:
	
	int iVec4[2];
	int iRec13[2];
	int fSampleRate;
	
  public:
	
	int getNumInputsThrusterDspSIG0() {
		return 0;
	}
	int getNumOutputsThrusterDspSIG0() {
		return 1;
	}
	
	void instanceInitThrusterDspSIG0(int sample_rate) {
		fSampleRate = sample_rate;
		for (int l17 = 0; l17 < 2; l17 = faust_wrap_add(l17, 1)) {
			iVec4[l17] = 0;
		}
		for (int l18 = 0; l18 < 2; l18 = faust_wrap_add(l18, 1)) {
			iRec13[l18] = 0;
		}
	}
	
	void fillThrusterDspSIG0(int count, float* table) {
		for (int i1 = 0; i1 < count; i1 = faust_wrap_add(i1, 1)) {
			iVec4[0] = 1;
			iRec13[0] = (faust_wrap_add(iVec4[1], iRec13[1])) % 65536;
			table[i1] = std::sin(9.58738e-05f * static_cast<float>(iRec13[0]));
			iVec4[1] = iVec4[0];
			iRec13[1] = iRec13[0];
		}
	}

};

static ThrusterDspSIG0* newThrusterDspSIG0() { return (ThrusterDspSIG0*)new ThrusterDspSIG0(); }
static void deleteThrusterDspSIG0(ThrusterDspSIG0* dsp) { delete dsp; }

static float ThrusterDsp_faustpower2_f(float value_f) {
	return value_f * value_f;
}
static float ftbl0ThrusterDspSIG0[65536];

class ThrusterDsp : public dsp {
	
 private:
	
	int iVec0[2];
	int fSampleRate;
	float fConst0;
	float fConst1;
	FAUSTFLOAT fHslider0;
	float fVec1[2];
	float fConst2;
	float fRec2[2];
	float fConst3;
	float fConst4;
	float fConst5;
	float fConst6;
	float fConst7;
	int iRec6[2];
	float fRec4[2];
	float fConst8;
	float fRec5[2];
	int iRec8[2];
	int iRec7[2];
	float fConst9;
	int iRec10[2];
	int iRec9[2];
	float fVec2[2];
	int IOTA0;
	float fVec3[4096];
	float fConst10;
	float fRec3[2];
	float fRec0[2];
	float fRec1[2];
	float fRec11[2];
	float fRec12[2];
	float fConst11;
	int iRec15[2];
	int iRec14[2];
	float fConst12;
	float fConst13;
	float fConst14;
	float fRec16[2];
	float fRec17[2];
	float fConst15;
	float fConst16;
	float fConst17;
	float fRec18[2];
	float fRec19[2];
	float fConst18;
	float fConst19;
	float fConst20;
	float fRec20[2];
	float fRec21[2];
	float fConst21;
	float fConst22;
	float fConst23;
	float fRec22[2];
	float fRec23[2];
	
 public:
	ThrusterDsp() {
	}
	
	ThrusterDsp(const ThrusterDsp&) = default;
	
	virtual ~ThrusterDsp() = default;
	
	ThrusterDsp& operator=(const ThrusterDsp&) = default;
	
	void metadata(Meta* m) { 
		m->declare("basics.lib/name", "Faust Basic Element Library");
		m->declare("basics.lib/version", "1.23.9");
		m->declare("compile_options", "-lang cpp -i -fpga-mem-th 4 -ct 1 -cn ThrusterDsp -es 1 -mcd 16 -mdd 1024 -mdy 33 -single -ftz 0");
		m->declare("filename", "thruster.dsp");
		m->declare("filters.lib/_tanhalf:author", "Julius O. Smith III");
		m->declare("filters.lib/_tanhalf:license", "MIT");
		m->declare("filters.lib/lowpass0_highpass1:author", "Julius O. Smith III");
		m->declare("filters.lib/lowpass0_highpass1:copyright", "Copyright (C) 2003-2019 by Julius O. Smith III <jos@ccrma.stanford.edu>");
		m->declare("filters.lib/lowpass0_highpass1:license", "LicenseRef-STK-4.3");
		m->declare("filters.lib/lowpass:author", "Julius O. Smith III");
		m->declare("filters.lib/lowpass:copyright", "Copyright (C) 2003-2019 by Julius O. Smith III <jos@ccrma.stanford.edu>");
		m->declare("filters.lib/lowpass:license", "LicenseRef-STK-4.3");
		m->declare("filters.lib/name", "Faust Filters Library");
		m->declare("filters.lib/resonbp:author", "Julius O. Smith III");
		m->declare("filters.lib/resonbp:copyright", "Copyright (C) 2003-2019 by Julius O. Smith III <jos@ccrma.stanford.edu>");
		m->declare("filters.lib/resonbp:license", "LicenseRef-STK-4.3");
		m->declare("filters.lib/svf:author", "Oleg Nesterov");
		m->declare("filters.lib/svf:copyright", "Copyright (C) 2020 Oleg Nesterov <oleg@redhat.com>");
		m->declare("filters.lib/svf:license", "LicenseRef-STK-4.3");
		m->declare("filters.lib/tf1s:author", "Julius O. Smith III");
		m->declare("filters.lib/tf1s:copyright", "Copyright (C) 2003-2019 by Julius O. Smith III <jos@ccrma.stanford.edu>");
		m->declare("filters.lib/tf1s:license", "LicenseRef-STK-4.3");
		m->declare("filters.lib/tf2s:author", "Julius O. Smith III");
		m->declare("filters.lib/tf2s:copyright", "Copyright (C) 2003-2019 by Julius O. Smith III <jos@ccrma.stanford.edu>");
		m->declare("filters.lib/tf2s:license", "LicenseRef-STK-4.3");
		m->declare("filters.lib/version", "1.13.4");
		m->declare("maths.lib/author", "GRAME");
		m->declare("maths.lib/copyright", "GRAME");
		m->declare("maths.lib/license", "LicenseRef-LGPL-2.1-or-later-with-Faust-exception");
		m->declare("maths.lib/name", "Faust Math Library");
		m->declare("maths.lib/version", "2.9.5");
		m->declare("name", "thruster");
		m->declare("noises.lib/name", "Faust Noise Generator Library");
		m->declare("noises.lib/version", "1.9.2");
		m->declare("oscillators.lib/lf_sawpos:author", "Bart Brouns, revised by Stéphane Letz");
		m->declare("oscillators.lib/lf_sawpos:licence", "LicenseRef-STK-4.3");
		m->declare("oscillators.lib/name", "Faust Oscillator Library");
		m->declare("oscillators.lib/saw2ptr:author", "Julius O. Smith III");
		m->declare("oscillators.lib/saw2ptr:license", "LicenseRef-STK-4.3");
		m->declare("oscillators.lib/sawN:author", "Julius O. Smith III");
		m->declare("oscillators.lib/sawN:license", "LicenseRef-STK-4.3");
		m->declare("oscillators.lib/version", "1.9.6");
		m->declare("platform.lib/name", "Generic Platform Library");
		m->declare("platform.lib/version", "1.3.2");
		m->declare("routes.lib/name", "Faust Signal Routing Library");
		m->declare("routes.lib/version", "1.4.1");
		m->declare("signals.lib/name", "Faust Routing Library");
		m->declare("signals.lib/version", "1.8.2");
	}

	virtual int getNumInputs() {
		return 0;
	}
	virtual int getNumOutputs() {
		return 1;
	}
	
	static void classInit(int sample_rate) {
		ThrusterDspSIG0* sig0 = newThrusterDspSIG0();
		sig0->instanceInitThrusterDspSIG0(sample_rate);
		sig0->fillThrusterDspSIG0(65536, ftbl0ThrusterDspSIG0);
		deleteThrusterDspSIG0(sig0);
	}
	
	virtual void instanceConstants(int sample_rate) {
		fSampleRate = sample_rate;
		fConst0 = std::min<float>(1.92e+05f, std::max<float>(5e+01f, static_cast<float>(fSampleRate)));
		fConst1 = 3.1415927f / fConst0;
		fConst2 = std::exp(-(5.0f / fConst0));
		fConst3 = 1.0f / fConst0;
		fConst4 = std::tan(std::max<float>(0.0f, std::min<float>(15.707963f / fConst0, 1.5676547f)));
		fConst5 = fConst4 + 1.4142135f;
		fConst6 = fConst4 * std::min<float>(3.4028235e+38f, 1.0f / (fConst4 * fConst5 + 1.0f));
		fConst7 = 2.0f * fConst6;
		fConst8 = 2.0f * fConst4;
		fConst9 = 0.175f * fConst0;
		fConst10 = 0.5f * fConst0;
		fConst11 = 3.0f / fConst0;
		fConst12 = std::tan(std::max<float>(0.0f, std::min<float>(543.49554f / fConst0, 1.5676547f)));
		fConst13 = fConst12 * std::min<float>(3.4028235e+38f, 1.0f / (fConst12 * (fConst12 + 0.05f) + 1.0f));
		fConst14 = -0.05f - fConst12;
		fConst15 = std::tan(std::max<float>(0.0f, std::min<float>(977.03534f / fConst0, 1.5676547f)));
		fConst16 = fConst15 * std::min<float>(3.4028235e+38f, 1.0f / (fConst15 * (fConst15 + 0.055555556f) + 1.0f));
		fConst17 = -0.055555556f - fConst15;
		fConst18 = std::tan(std::max<float>(0.0f, std::min<float>(1467.1238f / fConst0, 1.5676547f)));
		fConst19 = fConst18 * std::min<float>(3.4028235e+38f, 1.0f / (fConst18 * (fConst18 + 0.0625f) + 1.0f));
		fConst20 = -0.0625f - fConst18;
		fConst21 = std::tan(std::max<float>(0.0f, std::min<float>(3549.9998f / fConst0, 1.5676547f)));
		fConst22 = fConst21 * std::min<float>(3.4028235e+38f, 1.0f / (fConst21 * (fConst21 + 0.071428575f) + 1.0f));
		fConst23 = -0.071428575f - fConst21;
	}
	
	virtual void instanceResetUserInterface() {
		fHslider0 = static_cast<FAUSTFLOAT>(1.2e+03f);
	}
	
	virtual void instanceClear() {
		for (int l0 = 0; l0 < 2; l0 = faust_wrap_add(l0, 1)) {
			iVec0[l0] = 0;
		}
		for (int l1 = 0; l1 < 2; l1 = faust_wrap_add(l1, 1)) {
			fVec1[l1] = 0.0f;
		}
		for (int l2 = 0; l2 < 2; l2 = faust_wrap_add(l2, 1)) {
			fRec2[l2] = 0.0f;
		}
		for (int l3 = 0; l3 < 2; l3 = faust_wrap_add(l3, 1)) {
			iRec6[l3] = 0;
		}
		for (int l4 = 0; l4 < 2; l4 = faust_wrap_add(l4, 1)) {
			fRec4[l4] = 0.0f;
		}
		for (int l5 = 0; l5 < 2; l5 = faust_wrap_add(l5, 1)) {
			fRec5[l5] = 0.0f;
		}
		for (int l6 = 0; l6 < 2; l6 = faust_wrap_add(l6, 1)) {
			iRec8[l6] = 0;
		}
		for (int l7 = 0; l7 < 2; l7 = faust_wrap_add(l7, 1)) {
			iRec7[l7] = 0;
		}
		for (int l8 = 0; l8 < 2; l8 = faust_wrap_add(l8, 1)) {
			iRec10[l8] = 0;
		}
		for (int l9 = 0; l9 < 2; l9 = faust_wrap_add(l9, 1)) {
			iRec9[l9] = 0;
		}
		for (int l10 = 0; l10 < 2; l10 = faust_wrap_add(l10, 1)) {
			fVec2[l10] = 0.0f;
		}
		IOTA0 = 0;
		for (int l11 = 0; l11 < 4096; l11 = faust_wrap_add(l11, 1)) {
			fVec3[l11] = 0.0f;
		}
		for (int l12 = 0; l12 < 2; l12 = faust_wrap_add(l12, 1)) {
			fRec3[l12] = 0.0f;
		}
		for (int l13 = 0; l13 < 2; l13 = faust_wrap_add(l13, 1)) {
			fRec0[l13] = 0.0f;
		}
		for (int l14 = 0; l14 < 2; l14 = faust_wrap_add(l14, 1)) {
			fRec1[l14] = 0.0f;
		}
		for (int l15 = 0; l15 < 2; l15 = faust_wrap_add(l15, 1)) {
			fRec11[l15] = 0.0f;
		}
		for (int l16 = 0; l16 < 2; l16 = faust_wrap_add(l16, 1)) {
			fRec12[l16] = 0.0f;
		}
		for (int l19 = 0; l19 < 2; l19 = faust_wrap_add(l19, 1)) {
			iRec15[l19] = 0;
		}
		for (int l20 = 0; l20 < 2; l20 = faust_wrap_add(l20, 1)) {
			iRec14[l20] = 0;
		}
		for (int l21 = 0; l21 < 2; l21 = faust_wrap_add(l21, 1)) {
			fRec16[l21] = 0.0f;
		}
		for (int l22 = 0; l22 < 2; l22 = faust_wrap_add(l22, 1)) {
			fRec17[l22] = 0.0f;
		}
		for (int l23 = 0; l23 < 2; l23 = faust_wrap_add(l23, 1)) {
			fRec18[l23] = 0.0f;
		}
		for (int l24 = 0; l24 < 2; l24 = faust_wrap_add(l24, 1)) {
			fRec19[l24] = 0.0f;
		}
		for (int l25 = 0; l25 < 2; l25 = faust_wrap_add(l25, 1)) {
			fRec20[l25] = 0.0f;
		}
		for (int l26 = 0; l26 < 2; l26 = faust_wrap_add(l26, 1)) {
			fRec21[l26] = 0.0f;
		}
		for (int l27 = 0; l27 < 2; l27 = faust_wrap_add(l27, 1)) {
			fRec22[l27] = 0.0f;
		}
		for (int l28 = 0; l28 < 2; l28 = faust_wrap_add(l28, 1)) {
			fRec23[l28] = 0.0f;
		}
	}
	
	virtual void init(int sample_rate) {
		classInit(sample_rate);
		instanceInit(sample_rate);
	}
	
	virtual void instanceInit(int sample_rate) {
		instanceConstants(sample_rate);
		instanceResetUserInterface();
		instanceClear();
	}
	
	virtual ThrusterDsp* clone() {
		return new ThrusterDsp(*this);
	}
	
	virtual int getSampleRate() {
		return fSampleRate;
	}
	
	virtual void buildUserInterface(UI* ui_interface) {
		ui_interface->openVerticalBox("thruster");
		ui_interface->addHorizontalSlider("rpm", &fHslider0, FAUSTFLOAT(1.2e+03f), FAUSTFLOAT(3e+02f), FAUSTFLOAT(6e+03f), FAUSTFLOAT(1.0f));
		ui_interface->closeBox();
	}
	
	virtual void compute(int count, FAUSTFLOAT** RESTRICT inputs, FAUSTFLOAT** RESTRICT outputs) {
		FAUSTFLOAT* output0 = outputs[0];
		float fSlow0 = static_cast<float>(fHslider0);
		float fSlow1 = fSlow0 + -3e+02f;
		for (int i0 = 0; i0 < count; i0 = faust_wrap_add(i0, 1)) {
			iVec0[0] = 1;
			fVec1[0] = fSlow0;
			fRec2[0] = fConst2 * (std::min<float>(3.4028235e+38f, fVec1[1] - fSlow0) + fRec2[1]);
			float fTemp0 = fSlow0 + fRec2[0];
			float fTemp1 = 28.0f + 0.025f * fTemp0;
			float fTemp2 = fSlow1 + fRec2[0];
			float fTemp3 = 0.0001754386f * fTemp2;
			float fTemp4 = std::tan(std::max<float>(0.0f, std::min<float>(fConst1 * (6e+01f + 2.5f * fTemp1 + 1.4e+02f * (1.0f - fTemp3)), 1.5676547f)));
			iRec6[0] = faust_wrap_add(12345, faust_wrap_mul(1103515245, iRec6[1]));
			float fTemp5 = 4.656613e-10f * static_cast<float>(iRec6[0]);
			float fTemp6 = fTemp5 - (fConst5 * fRec4[1] + fRec5[1]);
			fRec4[0] = fRec4[1] + fConst7 * fTemp6;
			float fTemp7 = fRec4[1] + fConst6 * fTemp6;
			fRec5[0] = fRec5[1] + fConst8 * fTemp7;
			float fTemp8 = fTemp1 * (1.0f + 0.014f * (fRec5[1] + fConst4 * fTemp7));
			float fTemp9 = std::fabs(fTemp8);
			float fTemp10 = fConst3 * fTemp9;
			float fTemp11 = 1.0737418e+09f * (fTemp10 - std::floor(fTemp10));
			float fTemp12 = std::floor(fTemp11);
			int iTemp13 = faust_wrap_add(iRec8[1], static_cast<int>(16777216.0f * (fTemp11 - fTemp12)));
			iRec8[0] = iTemp13 & 16777215;
			int iTemp14 = faust_wrap_add(faust_wrap_add(iRec7[1], static_cast<int>(fTemp12)), iTemp13 >> 24);
			int iTemp15 = iTemp14 & 1073741823;
			iRec7[0] = iTemp15;
			float fTemp16 = static_cast<float>(iTemp15);
			int iTemp17 = faust_wrap_sub(1, iVec0[1]);
			float fTemp18 = std::max<float>(0.5f * fTemp8, 23.44895f);
			float fTemp19 = std::max<float>(2e+01f, std::fabs(fTemp18));
			float fTemp20 = fConst3 * fTemp19;
			float fTemp21 = 1.0737418e+09f * (fTemp20 - std::rint(fTemp20));
			float fTemp22 = std::floor(fTemp21);
			int iTemp23 = faust_wrap_add(iRec10[1], static_cast<int>(16777216.0f * (fTemp21 - fTemp22)));
			iRec10[0] = ((iTemp17) ? 0 : iTemp23 & 16777215);
			iRec9[0] = ((iTemp17) ? 0 : (faust_wrap_add(faust_wrap_add(iRec9[1], static_cast<int>(fTemp22)), iTemp23 >> 24)) & 1073741823);
			float fTemp24 = ThrusterDsp_faustpower2_f(-1.0f + 1.1920929e-07f * static_cast<float>(iRec9[0] >> 6));
			fVec2[0] = fTemp24;
			float fTemp25 = static_cast<float>(iVec0[1]) * (fTemp24 - fVec2[1]) / fTemp19;
			fVec3[IOTA0 & 4095] = fTemp25;
			float fTemp26 = std::max<float>(0.0f, std::min<float>(8191.0f, fConst10 / fTemp18));
			int iTemp27 = static_cast<int>(fTemp26);
			float fTemp28 = std::floor(fTemp26);
			float fTemp29 = 1.0f + fTemp4;
			float fTemp30 = fTemp4 * (-1.0f - (fTemp10 + fRec3[1]) + 2.0f * ((iTemp14 > 1073741823) ? 1.0f + 9.313226e-10f * fTemp16 * (1.0f - fConst0 / std::max<float>(1.1920929e-07f, fTemp9)) : 9.313226e-10f * fTemp16) + fConst9 * (fTemp25 - fVec3[(faust_wrap_sub(IOTA0, iTemp27)) & 4095] * (1.0f - fTemp26 + fTemp28) - (fTemp26 - fTemp28) * fVec3[(faust_wrap_sub(IOTA0, faust_wrap_add(1, iTemp27))) & 4095])) * std::min<float>(3.4028235e+38f, 1.0f / fTemp29);
			fRec3[0] = fRec3[1] + 2.0f * fTemp30;
			float fTemp31 = fTemp4 * (fRec3[1] + fTemp30 - (fTemp29 * fRec0[1] + fRec1[1])) * std::min<float>(3.4028235e+38f, 1.0f / (1.0f + fTemp4 * fTemp29));
			fRec0[0] = fRec0[1] + 2.0f * fTemp31;
			float fTemp32 = fTemp4 * (fRec0[1] + fTemp31);
			fRec1[0] = fRec1[1] + 2.0f * fTemp32;
			float fTemp33 = 8.77193e-05f * fTemp2;
			float fTemp34 = std::tan(std::max<float>(0.0f, std::min<float>(fConst1 * (7e+02f + 0.6f * fTemp0), 1.5676547f)));
			float fTemp35 = fRec11[1] + fTemp34 * (fTemp5 + fRec11[1] * (-0.6666667f - fTemp34) - fRec12[1]) * std::min<float>(3.4028235e+38f, 1.0f / (1.0f + fTemp34 * (0.6666667f + fTemp34)));
			fRec11[0] = 2.0f * fTemp35 - fRec11[1];
			fRec12[0] = 2.0f * (fRec12[1] + fTemp34 * fTemp35) - fRec12[1];
			float fTemp36 = fConst11 * fTemp8;
			float fTemp37 = 1.0737418e+09f * (fTemp36 - std::rint(fTemp36));
			float fTemp38 = std::floor(fTemp37);
			int iTemp39 = faust_wrap_add(iRec15[1], static_cast<int>(16777216.0f * (fTemp37 - fTemp38)));
			iRec15[0] = ((iTemp17) ? 0 : iTemp39 & 16777215);
			iRec14[0] = ((iTemp17) ? 0 : (faust_wrap_add(faust_wrap_add(iRec14[1], static_cast<int>(fTemp38)), iTemp39 >> 24)) & 1073741823);
			float fTemp40 = fRec16[1] + fConst13 * (fTemp5 + fConst14 * fRec16[1] - fRec17[1]);
			fRec16[0] = 2.0f * fTemp40 - fRec16[1];
			fRec17[0] = 2.0f * (fRec17[1] + fConst12 * fTemp40) - fRec17[1];
			float fTemp41 = fRec18[1] + fConst16 * (fTemp5 + fConst17 * fRec18[1] - fRec19[1]);
			fRec18[0] = 2.0f * fTemp41 - fRec18[1];
			fRec19[0] = 2.0f * (fRec19[1] + fConst15 * fTemp41) - fRec19[1];
			float fTemp42 = fRec20[1] + fConst19 * (fTemp5 + fConst20 * fRec20[1] - fRec21[1]);
			fRec20[0] = 2.0f * fTemp42 - fRec20[1];
			fRec21[0] = 2.0f * (fRec21[1] + fConst18 * fTemp42) - fRec21[1];
			float fTemp43 = fRec22[1] + fConst22 * (fTemp5 + fConst23 * fRec22[1] - fRec23[1]);
			fRec22[0] = 2.0f * fTemp43 - fRec22[1];
			fRec23[0] = 2.0f * (fRec23[1] + fConst21 * fTemp43) - fRec23[1];
			output0[i0] = static_cast<FAUSTFLOAT>(0.425f * tanhf(((fRec1[1] + fTemp32) * (0.5f + fTemp33) + 0.5f * fTemp35 * (1.0f + ThrusterDsp_faustpower2_f(ftbl0ThrusterDspSIG0[std::max<int>(0, std::min<int>(static_cast<int>(0.00390625f * static_cast<float>(iRec14[0] >> 6)), 65535))])) * (0.05f + 0.00030701753f * fTemp2) + 0.25f * tanhf(12.0f * (fTemp40 * (0.3f + 0.00012280702f * fTemp2) + 0.8f * fTemp41 + 0.6f * fTemp42 + 0.4f * fTemp43) * (0.015f + 0.985f * ThrusterDsp_faustpower2_f(fTemp3)))) * (0.4f + 0.000105263156f * fTemp2) * (0.9f - fTemp33)));
			iVec0[1] = iVec0[0];
			fVec1[1] = fVec1[0];
			fRec2[1] = fRec2[0];
			iRec6[1] = iRec6[0];
			fRec4[1] = fRec4[0];
			fRec5[1] = fRec5[0];
			iRec8[1] = iRec8[0];
			iRec7[1] = iRec7[0];
			iRec10[1] = iRec10[0];
			iRec9[1] = iRec9[0];
			fVec2[1] = fVec2[0];
			IOTA0 = faust_wrap_add(IOTA0, 1);
			fRec3[1] = fRec3[0];
			fRec0[1] = fRec0[0];
			fRec1[1] = fRec1[0];
			fRec11[1] = fRec11[0];
			fRec12[1] = fRec12[0];
			iRec15[1] = iRec15[0];
			iRec14[1] = iRec14[0];
			fRec16[1] = fRec16[0];
			fRec17[1] = fRec17[0];
			fRec18[1] = fRec18[0];
			fRec19[1] = fRec19[0];
			fRec20[1] = fRec20[0];
			fRec21[1] = fRec21[0];
			fRec22[1] = fRec22[0];
			fRec23[1] = fRec23[0];
		}
	}

};

#endif
