namespace AquaSynth.Dsl;

public static class BuiltInScripts
{
    public const string PatchScriptExample = """
        # Commands can span lines. A field-only line belongs to the command above it.
        patch
            gain=0.7
            soft_clip=true

        mod name=wobble
            wave=sine
            hz=5
            pitch=0.01
            formant_mix=0.08

        bus name=shimmer
            wave=triangle
            hz=2
            to=gain:0.08,lpf:-0.04,formant_mix:0.12

        voice
            wave=sine
            freq=220
            gain=0.12
            attack=0.002
            sustain=0.03
            decay=0.2
            vibrato=0.02
            vibrato_hz=5
            formants=620:90:1,1040:150:0.8
            formant_mix=0.45

        voice
            wave=triangle
            freq=440
            gain=0.04
            sustain=0.02
            decay=0.18
            lpf=0.7
            hpf=0.02

        sfxr
            preset=laser
            mutate_seed=9
            mutate=0.01
        """;

    public static readonly IReadOnlyList<string> ClassicSfxrNames =
    [
        "pickup",
        "laser",
        "explosion",
        "powerup",
        "hit",
        "jump",
        "blip"
    ];

    public const string ClassicSfxrPrimitiveGolfScript = "pickup;laser;explosion;powerup;hit;jump;blip";

    public static readonly IReadOnlyList<(string Name, string Script)> ClassicSfxrPrimitiveGolfScripts =
    [
        ("pickup", """
            voice
                wave=square
                freq=148.7934
                gain=0.319
                sustain=0.01451247166
                decay=0.1306122449
                sustain_level=0.6896552
                drive=0.201
                arp_delay=0.081844
                arp_mult=1.116121255
            """),
        ("laser", """
            voice
                wave=saw
                freq=229.0554
                gain=0.22
                sustain=0.08185941043
                decay=0.07346938776
                pitch_ramp=0.703836
                duty=0.31
                duty_ramp=0.056
                hpf=0.04
                phaser=0.0001458
                phaser_ramp=-0.000504
                drive=0.12
            """),
        ("explosion", """
            patch repeat=0.174744
            voice
                wave=noise
                freq=20
                gain=0.3344
                sustain=0.1907029478
                decay=0.293877551
                sustain_level=0.6578947
                pitch_ramp=0.0320625
                phaser=-0.0008712
                phaser_ramp=-0.00035
                vibrato=0.11
                vibrato_hz=3.4112
                noise=0.35
                drive=0.2136
                tremolo=0.0264
                tremolo_hz=13.04
            """),
        ("powerup", """
            patch repeat=0.11315
            voice
                wave=sine
                freq=57.5946
                gain=0.22
                sustain=0.1306122449
                decay=0.1777777778
                pitch_ramp=-0.208544
                vibrato=0.09
                vibrato_hz=3.9602
                drive=0.12
                tremolo=0.0216
                tremolo_hz=13.94
            """),
        ("hit", """
            voice
                wave=noise
                freq=51.4206
                gain=0.22
                sustain=0.00566893424
                decay=0.09070294785
                pitch_ramp=1.050624
                hpf=0.12
                noise=0.35
                drive=0.12
            """),
        ("jump", """
            voice
                wave=square
                freq=78.2334
                gain=0.22
                sustain=0.1097505669
                decay=0.07346938776
                pitch_ramp=-0.101156
                duty=0.38
                lpf=0.72
                hpf=0.05
                drive=0.12
            """),
        ("blip", """
            voice
                wave=sine
                freq=78.2334
                gain=0.22
                sustain=0.03832199546
                decay=0.01451247166
                hpf=0.1
                drive=0.12
            """)
    ];

    public const string ClassicSfxrAbstractGolfScript = """
        defaults gain=0.22 drive=0.12
        template name=Noise
            wave=noise
            noise=0.35

        voice
            wave=square
            freq=148.7934
            gain=0.319
            sustain=0.01451247166
            decay=0.1306122449
            sustain_level=0.6896552
            drive=0.201
            arp_delay=0.081844
            arp_mult=1.116121255

        voice
            wave=saw
            freq=229.0554
            sustain=0.08185941043
            decay=0.07346938776
            pitch_ramp=0.703836
            duty=0.31
            duty_ramp=0.056
            hpf=0.04
            phaser=0.0001458
            phaser_ramp=-0.000504

        patch repeat=0.174744
        voice
            use=Noise
            freq=20
            gain=0.3344
            sustain=0.1907029478
            decay=0.293877551
            sustain_level=0.6578947
            pitch_ramp=0.0320625
            phaser=-0.0008712
            phaser_ramp=-0.00035
            vibrato=0.11
            vibrato_hz=3.4112
            drive=0.2136
            tremolo=0.0264
            tremolo_hz=13.04

        patch repeat=0.11315
        voice
            wave=sine
            freq=57.5946
            sustain=0.1306122449
            decay=0.1777777778
            pitch_ramp=-0.208544
            vibrato=0.09
            vibrato_hz=3.9602
            tremolo=0.0216
            tremolo_hz=13.94

        voice
            use=Noise
            freq=51.4206
            sustain=0.00566893424
            decay=0.09070294785
            pitch_ramp=1.050624
            hpf=0.12

        voice
            wave=square
            freq=78.2334
            sustain=0.1097505669
            decay=0.07346938776
            pitch_ramp=-0.101156
            duty=0.38
            lpf=0.72
            hpf=0.05

        voice
            wave=sine
            freq=78.2334
            sustain=0.03832199546
            decay=0.01451247166
            hpf=0.1
        """;

    public static readonly IReadOnlyList<string> BfxrNames = ["coin-spark", "shield-pop", "ui-bloom", "portal-chirp"];

    public static readonly IReadOnlyList<(string Name, string Script)> BfxrReferenceScripts =
    [
        ("coin-spark", """
            defaults
                wave=sine
                gain=0.18
                attack=0
                sustain=0.022
                decay=0.42
                hpf=0.05
                drive=0.08

            voice
                freq=1180
                fm=5
                fm_index=3.2
                fm_decay=0.16

            voice
                freq=1770
                gain=0.09
                fm=7
                fm_index=1.7
                fm_decay=0.11
            """),
        ("shield-pop", """
            defaults
                wave=triangle
                gain=0.264
                sustain=0.04
                decay=0.36
                sustain_level=0.7575758
                lpf=0.68
                hpf=0.03
                drive=0.12

            voice
                freq=260
                pitch_ramp=-0.75
                vibrato=0.04
                vibrato_hz=7.2

            voice
                wave=noise
                freq=900
                gain=0.05
                sustain=0.018
                decay=0.16
                noise=0.7
                hpf=0.4
            """),
        ("ui-bloom", """
            patch repeat=0.18
            defaults
                wave=sine
                gain=0.1464
                sustain=0.05
                decay=0.5
                sustain_level=0.8196721
                lpf=0.78
                drive=0.06

            voice
                freq=392
                pitch_ramp=0.18
                arp_delay=0.08
                arp_mult=1.25

            voice
                freq=588
                gain=0.06
                attack=0.004
                sustain=0.04
                decay=0.38
            """),
        ("portal-chirp", """
            defaults
                wave=saw
                freq=320
                gain=0.14
                sustain=0.09
                decay=0.28
                pitch_ramp=0.85
                duty=0.38
                hpf=0.08
                lpf=0.72
                drive=0.16
                fold=0.08

            mod name=waver
                wave=sine
                hz=9
                pitch=0.018
                duty=0.06
                lpf=0.08

            voice
            voice wave=square freq=640 gain=0.05 duty=0.44
            """)
    ];

    public static readonly IReadOnlyList<string> Classic808Names = ["kick", "snare", "clap", "hat", "tom", "cowbell"];

    public static readonly IReadOnlyList<(string Name, string Script)> Classic808PrimitiveGolfScripts =
    [
        ("kick", """
            defaults wave=sine gain=1.32 drive=0.18
            voice
                freq=58
                sustain=0.045
                decay=0.42
                sustain_level=0.6060606
                pitch_ramp=-3.8
                min_freq=32
                lpf=0.85
            """),
        ("snare", """
            defaults drive=0.12
            template name=Noise
                wave=noise
                noise=0.85
                hpf=0.45
                lpf=0.55

            voice
                wave=sine
                freq=180
                gain=0.08
                sustain=0.02
                decay=0.12
                pitch_ramp=-1.2

            voice
                use=Noise
                freq=140
                gain=0.55
                sustain=0.035
                decay=0.2
            """),
        ("clap", """
            defaults
                wave=noise
                noise=0.95
                hpf=0.55
                lpf=0.42
                gain=0.22
                decay=0.11
                drive=0.1

            voice freq=1800 sustain=0.018 phaser=0.004
            voice freq=2200 sustain=0.022 phaser=0.008
            voice freq=2600 sustain=0.028 phaser=0.012
            """),
        ("hat", """
            defaults hpf=0.9 lpf=0.24 drive=0.05
            voice
                wave=noise
                freq=9000
                gain=0.16
                sustain=0.006
                decay=0.055
                noise=1

            voice
                wave=square
                freq=6800
                gain=0.045
                sustain=0.005
                decay=0.04
            """),
        ("tom", """
            defaults wave=sine gain=0.704 drive=0.12
            voice
                freq=115
                sustain=0.055
                decay=0.34
                sustain_level=0.78125
                pitch_ramp=-1.45
                min_freq=62
                lpf=0.78
            """),
        ("cowbell", """
            defaults wave=square hpf=0.18 lpf=0.82 drive=0.16
            voice
                freq=540
                gain=0.16
                sustain=0.05
                decay=0.18
                duty=0.43

            voice
                freq=800
                gain=0.12
                sustain=0.045
                decay=0.16
                duty=0.47
            """)
    ];

    public static readonly IReadOnlyList<string> FmBellNames = ["bell", "chime", "coin", "gong"];

    public static readonly IReadOnlyList<(string Name, string Script)> FmBellPrimitiveGolfScripts =
    [
        ("bell", """
            defaults wave=sine gain=0.24 attack=0.002 sustain=0.04 decay=1.2 lpf=0.9
            template name=Overtone
                fm=4.1
                fm_decay=0.55

            voice use=Overtone freq=440 fm_index=5.8
            voice use=Overtone freq=880 gain=0.08 fm_index=2.4
            """),
        ("chime", """
            defaults wave=sine gain=0.18 attack=0.001 sustain=0.025 decay=0.9 hpf=0.02
            template name=Overtone
                fm=3
                fm_decay=0.38

            voice use=Overtone freq=660 fm_index=4.2
            voice use=Overtone freq=990 gain=0.07 fm_index=2.1
            """),
        ("coin", """
            defaults wave=sine attack=0 sustain=0.02 decay=0.45 hpf=0.04 drive=0.08
            voice freq=1200 gain=0.18 fm=5 fm_index=3.4 fm_decay=0.18
            voice freq=1800 gain=0.09 fm=7 fm_index=1.8 fm_decay=0.12
            """),
        ("gong", """
            defaults wave=sine attack=0.003 sustain=0.08 decay=1.6 lpf=0.82 drive=0.1
            voice freq=196 gain=0.24 fm=2.414 fm_index=7.2 fm_decay=0.9
            voice freq=311 gain=0.12 fm=3.73 fm_index=4.1 fm_decay=0.7
            """)
    ];

    public static readonly IReadOnlyList<string> WobbleBassNames = ["talker", "growl", "yoy", "neuro"];

    public static readonly IReadOnlyList<(string Name, string Script)> WobbleBassPrimitiveGolfScripts =
    [
        ("talker", """
            defaults
                wave=saw
                freq=55
                gain=0.18
                sustain=0.8
                decay=0.25
                lpf=0.34
                hpf=0.02
                drive=0.3
                fold=0.08
                fm=2
                fm_index=0.8
                fm_decay=0.7
                formants=520:90:0.7,1250:170:1,2600:320:0.45
                formant_mix=0.35

            mod name=wobble
                hz=4
                wave=triangle
                gain=0.42
                lpf=0.48
                formant_mix=0.38
                fm_index=1.6
                drive=0.2
                fold=0.14

            voice
            voice freq=110 gain=0.08 duty=0.42
            """),
        ("growl", """
            defaults
                wave=saw
                freq=44
                gain=0.2
                sustain=0.7
                decay=0.3
                lpf=0.28
                hpf=0.01
                drive=0.42
                fold=0.18
                fm=1.5
                fm_index=1.2
                fm_decay=0.45
                noise=0.05

            mod name=wobble
                hz=6
                wave=sine
                gain=0.32
                lpf=0.55
                pitch=0.035
                drive=0.28
                fold=0.22
                noise=0.08
                fm_index=2.2

            voice
            voice wave=square freq=88 gain=0.09 duty=0.36
            """),
        ("yoy", """
            defaults
                wave=square
                freq=62
                gain=0.16
                sustain=0.65
                decay=0.22
                lpf=0.3
                hpf=0.03
                drive=0.25
                fm=3
                fm_index=0.7
                fm_decay=0.5
                formants=400:80:0.6,900:120:1,2100:260:0.4
                formant_mix=0.28

            mod name=wobble
                hz=5
                wave=square
                gain=0.5
                lpf=0.5
                formant_mix=0.45
                pitch=0.025
                duty=0.08

            voice
            voice wave=saw freq=124 gain=0.07
            """),
        ("neuro", """
            defaults
                wave=saw
                freq=49
                gain=0.16
                sustain=0.75
                decay=0.28
                lpf=0.25
                hpf=0.04
                drive=0.38
                fold=0.24
                fm=2.7
                fm_index=1.5
                fm_decay=0.35
                noise=0.04

            mod name=wobble
                hz=7
                wave=hold
                gain=0.28
                lpf=0.52
                pitch=0.04
                drive=0.25
                fold=0.3
                noise=0.1
                fm_index=2.8

            voice
            voice wave=triangle freq=147 gain=0.06
            """)
    ];

    public static readonly IReadOnlyList<string> AdvancedNames = ["aurora-pad", "machine-breath", "glass-creature", "ritual-sequence"];

    public static readonly IReadOnlyList<string> Dx7StyleNames = ["algo32-additive-organ", "algo8-bright-pair", "public-domain-mc-mm-5-3"];

    public static readonly IReadOnlyList<string> ZynStyleNames = ["additive-lead", "pad-texture", "vocal-layer"];

    public const string PinkTromboneProxy = """
        patch
            gain=0.82
            soft_clip=true

        param name=frequency path=/pink/frequency default=140 min=10 max=600 step=0.01 unit=Hz rate=audio
        param name=intensity path=/pink/intensity default=0.7 min=0 max=1 step=0.001
        param name=tenseness path=/pink/tenseness default=0.6 min=0 max=1 step=0.001
        param name=tongue_index path=/pink/tongue/index default=12.9 min=0 max=44 step=0.001 unit=cell
        param name=tongue_diameter path=/pink/tongue/diameter default=2.43 min=0 max=4 step=0.001 unit=diameter
        param name=velum path=/pink/velum default=0.01 min=0.01 max=0.4 step=0.001 unit=diameter
        param name=constriction_index path=/pink/constriction/index default=32 min=0 max=44 step=0.001 unit=cell
        param name=constriction_diameter path=/pink/constriction/diameter default=1 min=-1 max=4 step=0.001 unit=diameter

        defaults
            wave=saw
            freq=@/pink/frequency
            gain=@/pink/intensity
            attack=0.004
            sustain=0.18
            decay=0.08
            lpf=0.64
            lpf_q=@/pink/tenseness
            tremolo=0.02
            tremolo_hz=6
            formants=520:90:0.8,1250:170:1,2600:320:0.45
            formant_mix=0.42

        voice

        voice
            wave=noise
            freq=2500
            gain=0.04
            noise=0.8
            hpf=0.45
            lpf=0.7
            formants=1800:220:0.8,3600:480:0.55
            formant_mix=0.35

        voice
            wave=sine
            freq=360
            gain=@/pink/velum
            formants=300:80:1,900:180:0.45
            formant_mix=0.55
        """;

    public const string Dx7StyleAlgorithm32AdditiveOrgan = """
        patch
            gain=0.58
            soft_clip=true

        param
            name=drawbar
            path=/macro/drawbar
            default=0.78
            min=0
            max=1
            step=0.001
            unit=normalized

        param
            name=shimmer
            path=/macro/shimmer
            default=0.08
            min=0
            max=0.3
            step=0.001
            unit=normalized

        defaults
            wave=sine
            attack=0.002
            sustain=0.48
            decay=0.32
            lpf=0.96
            hpf=0.015
            drive=0.03

        voice freq=196 gain=0.14
        voice freq=392 gain=@/macro/drawbar
        voice freq=588 gain=0.12
        voice freq=784 gain=0.08
        voice freq=980 gain=0.055
        voice
            freq=1176
            gain=0.035
            fold=@/macro/shimmer
            fm=1
            fm_index=0.18
            fm_decay=0.24
        """;

    public const string Dx7StyleAlgorithm8BrightPair = """
        patch
            gain=0.52
            soft_clip=true

        param
            name=brightness
            path=/macro/brightness
            default=2.6
            min=0
            max=5
            step=0.001
            unit=index

        param
            name=strike
            path=/macro/strike
            default=0.18
            min=0.02
            max=0.6
            step=0.001
            unit=seconds

        defaults
            wave=sine
            attack=0.001
            sustain=0.045
            decay=@/macro/strike
            hpf=0.02
            lpf=0.9

        opgraph
            name=dx7_algo8_core
            freq=330
            gain=0.18

        operator name=op6
            ratio=4
            level=0.9
            env=ad:0.035:0.18

        operator name=op5
            ratio=3
            level=0.82
            env=ad:0.04:0.2

        operator name=op4
            ratio=2
            level=0.7
            feedback=0.18
            env=ad:0.05:0.24

        operator name=op3
            ratio=2
            level=0.6
            env=ad:0.06:0.3

        operator name=op2
            ratio=1
            level=0.75
            env=ad:0.045:0.2

        operator name=op1
            ratio=1
            level=0.82
            env=adsr:0.005:0.08:0.68:0.34

        route from=op6 to=op5 index=1.1
        route from=op5 to=op3 index=0.9
        route from=op4 to=op3 index=0.75
        route from=op2 to=op1 index=0.85
        carrier name=op1
        carrier name=op3

        voice
            freq=330
            gain=0.04
            fm=2
            fm_index=@/macro/brightness
            fm_decay=0.22

        voice
            freq=660
            gain=0.025
            drive=0.08
            fold=0.05
        """;

    public const string Dx7StylePublicDomainMcMm53 = """
        patch
            gain=0.82
            soft_clip=true

        voice
            wave=sine
            freq=654.0639
            gain=1
            attack=0.004
            sustain=0.08
            env_decay=0.008
            release=0.004
        """;

    public const string ZynStyleAdditiveLead = """
        patch
            gain=0.72
            soft_clip=true

        param
            name=brightness
            path=/macro/brightness
            default=0.86
            min=0
            max=1
            step=0.001

        param
            name=motion
            path=/macro/motion
            default=0.018
            min=0
            max=0.05
            step=0.001
            unit=ratio

        defaults
            wave=sine
            attack=0.006
            sustain=0.52
            decay=0.34
            sustain_level=0.72
            lpf=@/macro/brightness
            hpf=0.01
            vibrato=@/macro/motion
            vibrato_hz=5.5

        layer name=body engine=add min_key=36 max_key=96 gain=0.16
        layer name=shine engine=add min_key=48 max_key=108 gain=0.045

        harmonics layer=body root=220 partials=1:0.16,2:0.075
        harmonics layer=shine root=220 partials=3:0.045,4:0.025
        """;

    public const string ZynStylePadTexture = """
        patch
            gain=1.1

        param
            name=spread
            path=/macro/spread
            default=0.006
            min=0
            max=0.04
            step=0.001
            unit=ratio

        defaults
            wave=sine
            attack=0.28
            sustain=1.4
            decay=1.2
            sustain_level=0.9
            lpf=1
            hpf=0

        mod
            name=slow_air
            wave=sine
            hz=0.18
            lpf=0.18
            gain=0.05

        layer name=pad_low engine=pad min_key=36 max_key=84 gain=0.18 vibrato=0 vibrato_hz=0.09 env=rl rates=0.28,0.42,0.7,1.2 levels=1,0.94,0.88,0 curves=lin,exp,lin,lin gate=2.18
        layer name=pad_high engine=pad min_key=48 max_key=108 gain=0 vibrato=0 vibrato_hz=0.11 env=rl rates=0.28,0.42,0.7,1.2 levels=1,0.94,0.88,0 curves=lin,exp,lin,lin gate=2.18

        spectrum layer=pad_low root=77.7813 freq=261.6256 spread=0 partials=1:0.16,1.5:0.026,2:0.02
        spectrum layer=pad_high root=77.7813 freq=261.6256 spread=0 partials=1:0
        """;

    public const string ZynStyleVocalLayer = """
        patch
            gain=0.64
            soft_clip=true

        param
            name=vowel
            path=/macro/vowel
            default=0.58
            min=0
            max=1
            step=0.001

        defaults
            attack=0.08
            sustain=0.95
            decay=0.65
            sustain_level=0.7
            lpf=0.72
            hpf=0.04

        mod
            name=vowel_sway
            wave=sine
            hz=0.7
            formant_mix=0.22
            gain=0.04

        layer name=air engine=add min_key=48 max_key=108 gain=0.11 send=formant env=rl rates=0.08,0.24,0.58,0.55 levels=1,0.82,0.7,0 curves=lin,exp,lin,lin gate=0.9
        layer name=body engine=sub min_key=24 max_key=72 gain=0.075 env=rl rates=0.12,0.28,0.55,0.6 levels=1,0.78,0.64,0 curves=lin,exp,lin,lin gate=0.95
        layer name=breath engine=add min_key=0 max_key=127 gain=0.018 wave=noise noise=0.75 hpf=0.58 lpf=0.62 send=formant env=rl rates=0.18,0.25,0.48,0.42 levels=1,0.65,0.38,0 curves=lin,lin,exp,lin gate=0.85

        voice
            layer=air
            wave=saw
            freq=174.614
            formants=520:90:0.9,1080:150:1,2450:280:0.35
            formant_mix=@/macro/vowel

        voice
            layer=body
            wave=triangle
            freq=87.307
            lpf=0.48
            drive=0.08

        voice
            layer=breath
            freq=2600
            formants=700:120:0.5,1600:220:0.6
            formant_mix=0.3
        """;

    public static readonly IReadOnlyList<(string Name, string Script)> AdvancedReferenceScripts =
    [
        ("aurora-pad", """
            patch
                gain=0.58
                soft_clip=true

            defaults
                wave=sine
                attack=0.018
                sustain=1.15
                decay=1.6
                lpf=0.86
                hpf=0.018
                drive=0.05

            template name=GlassPartial
                fm=2.414
                fm_index=1.4
                fm_decay=0.8
                formants=420:80:0.25,1180:180:0.75,2450:360:0.42
                formant_mix=0.18

            mod name=tide
                wave=sine
                hz=0.21
                gain=0.08
                pitch=0.012
                lpf=0.09
                formant_mix=0.16
                fm_index=0.35

            mod name=shiver
                wave=triangle
                hz=4.6
                pitch=0.004
                drive=0.035

            voice use=GlassPartial freq=220 gain=0.12
            voice use=GlassPartial freq=330 gain=0.08 fm=3.01 fm_index=0.9
            voice use=GlassPartial freq=440 gain=0.06 fm=1.618 fm_index=0.7 formant_mix=0.26
            voice wave=noise freq=1600 gain=0.018 sustain=1.4 decay=2.0 noise=0.72 hpf=0.62 lpf=0.38
            """),
        ("machine-breath", """
            patch
                gain=0.7
                soft_clip=true

            defaults
                wave=saw
                freq=72
                gain=0.1
                attack=0.004
                sustain=0.72
                decay=0.55
                lpf=0.28
                hpf=0.025
                drive=0.34
                fold=0.12
                fm=1.5
                fm_index=0.8
                fm_decay=0.45

            mod name=piston
                wave=sample_hold
                hz=5.5
                gain=0.16
                pitch=0.028
                lpf=0.24
                drive=0.18
                fold=0.16

            mod name=valve
                wave=square
                hz=2.75
                noise=0.18
                hpf=0.12
                formant_mix=0.25

            voice
                formants=290:55:0.55,760:120:1,1780:260:0.5
                formant_mix=0.22

            voice
                wave=square
                freq=36
                gain=0.13
                duty=0.37
                pitch_ramp=-0.18

            voice
                wave=noise
                freq=700
                gain=0.055
                sustain=0.5
                decay=0.35
                noise=0.8
                hpf=0.42
                lpf=0.45

            voice
                wave=triangle
                freq=144
                gain=0.045
                sustain=0.62
                decay=0.48
                fm=2.25
                fm_index=1.1
                fm_decay=0.5
                formants=510:90:0.35,1320:170:0.9,2100:300:0.45
                formant_mix=0.18
            """),
        ("glass-creature", """
            patch
                gain=0.52
                soft_clip=true

            defaults
                wave=triangle
                attack=0.002
                sustain=0.18
                decay=1.05
                lpf=0.92
                hpf=0.03
                drive=0.08

            template name=Ping
                fm=3.5
                fm_index=2.8
                fm_decay=0.5
                vibrato=0.018
                vibrato_hz=6.4

            mod name=blink
                wave=sine
                hz=1.8
                pitch=0.02
                gain=0.18
                fm_index=0.8

            voice use=Ping freq=523.25 gain=0.1344 sustain_level=0.8928571
            voice use=Ping freq=659.25 gain=0.08 sustain=0.11 decay=0.84 fm=4.2
            voice use=Ping freq=783.99 gain=0.07 sustain=0.08 decay=0.72 fm_index=1.9
            voice wave=sine freq=261.63 gain=0.045 sustain=0.7 decay=1.3 fm=1.5 fm_index=0.65 fm_decay=1.2
            voice wave=noise freq=2400 gain=0.012 sustain=0.04 decay=0.55 noise=0.9 hpf=0.72 lpf=0.5
            """),
        ("ritual-sequence", """
            patch
                gain=0.62
                repeat=0.42
                soft_clip=true

            defaults
                wave=sine
                gain=0.1534
                attack=0.001
                sustain=0.045
                decay=0.5
                sustain_level=0.8474576
                lpf=0.82
                hpf=0.035
                drive=0.1

            template name=Step
                fm=2
                fm_index=1.8
                fm_decay=0.32
                arp_delay=0.065
                arp_mult=1.4983

            bus name=cycle
                wave=triangle
                hz=2.38
                to=gain:0.2,pitch:0.018,lpf:0.12,fm_index:0.75

            voice use=Step freq=196
            voice use=Step freq=293.66 gain=0.09 sustain=0.035 decay=0.38 arp_mult=1.3348
            voice use=Step freq=392 gain=0.07 sustain=0.028 decay=0.32 arp_mult=1.2599
            voice wave=noise freq=3100 gain=0.025 sustain=0.018 decay=0.16 noise=1 hpf=0.68 lpf=0.46
            """)
    ];

    public static IEnumerable<(string Family, string Name, string Script)> ReferenceScripts()
    {
        foreach (var item in ClassicSfxrPrimitiveGolfScripts) yield return ("sfxr", item.Name, item.Script);
        foreach (var item in BfxrReferenceScripts) yield return ("bfxr", item.Name, item.Script);
        foreach (var item in Classic808PrimitiveGolfScripts) yield return ("808", item.Name, item.Script);
        foreach (var item in FmBellPrimitiveGolfScripts) yield return ("fm-bell", item.Name, item.Script);
        foreach (var item in WobbleBassPrimitiveGolfScripts) yield return ("wobble-bass", item.Name, item.Script);
        yield return ("pink-trombone", "current-aquasynth-proxy", PinkTromboneProxy);
        foreach (var item in ReferenceRebuildCatalog.Dx7Rebuilds) yield return ("dx7", item.Name, item.Script);
        foreach (var item in ReferenceRebuildCatalog.ZynRebuilds) yield return ("zynaddsubfx", item.Name, item.Script);
        foreach (var item in AdvancedReferenceScripts) yield return ("advanced", item.Name, item.Script);
    }

    public static IEnumerable<(string Family, string Name, string Script)> PrimitiveGolfScripts() => ReferenceScripts();
}
