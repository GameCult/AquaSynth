using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AquaSynth.Dsl;

namespace AquaSynth.Faust;

public sealed record AquaSynthNativeOptions(
    int SampleRate = 44100,
    string? FaustHome = null,
    string? DspSourceDirectory = null,
    float MinRenderSeconds = 0.05f,
    float MaxRenderSeconds = 3.0f);

public sealed record AquaSynthCompileIdentity(
    string Id,
    string FaustName,
    string Script,
    int Revision = 0)
{
    public string CompileKey => AquaSynthNativeCompiler.CompileKey(this);
}

public sealed record AquaSynthNativeManifest(
    string Id,
    string FaustName,
    string CompileKey,
    int Revision,
    int SampleRate,
    int OutputCount,
    int FrameCount,
    float DurationSeconds,
    double CompileMilliseconds,
    string FaustVersion,
    string FaustHome,
    string? DspSourcePath);

public sealed class AquaSynthCompiledPatch : IDisposable
{
    private readonly FaustNativeToolchain toolchain;
    private readonly IntPtr factory;
    private bool disposed;

    internal AquaSynthCompiledPatch(
        FaustNativeToolchain toolchain,
        IntPtr factory,
        AquaSynthNativeManifest manifest,
        IReadOnlyList<string> controlPaths)
    {
        this.toolchain = toolchain;
        this.factory = factory;
        Manifest = manifest;
        ControlPaths = controlPaths;
    }

    public AquaSynthNativeManifest Manifest { get; }

    public IReadOnlyList<string> ControlPaths { get; }

    public float[] Render(float gain = 1.0f) => Render(null, gain);

    public float[] Render(IReadOnlyDictionary<string, float>? controls, float gain = 1.0f)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var dsp = toolchain.CreateInstance(factory);
        try
        {
            toolchain.InitInstance(dsp, Manifest.SampleRate);
            if (controls is not null)
            {
                toolchain.SetControls(dsp, controls);
            }

            var outputs = new float[Manifest.OutputCount][];
            var outputPointers = new IntPtr[Manifest.OutputCount];
            var handles = new GCHandle[Manifest.OutputCount];
            try
            {
                for (var channel = 0; channel < Manifest.OutputCount; channel++)
                {
                    outputs[channel] = new float[Manifest.FrameCount];
                    handles[channel] = GCHandle.Alloc(outputs[channel], GCHandleType.Pinned);
                    outputPointers[channel] = handles[channel].AddrOfPinnedObject();
                }

                var pointersHandle = GCHandle.Alloc(outputPointers, GCHandleType.Pinned);
                try
                {
                    toolchain.Compute(dsp, Manifest.FrameCount, IntPtr.Zero, pointersHandle.AddrOfPinnedObject());
                }
                finally
                {
                    pointersHandle.Free();
                }
            }
            finally
            {
                foreach (var handle in handles)
                {
                    if (handle.IsAllocated)
                    {
                        handle.Free();
                    }
                }
            }

            var mono = new float[Manifest.FrameCount];
            for (var index = 0; index < Manifest.FrameCount; index++)
            {
                var sample = 0.0f;
                for (var channel = 0; channel < Manifest.OutputCount; channel++)
                {
                    sample += outputs[channel][index];
                }

                mono[index] = Math.Clamp(sample / Manifest.OutputCount * gain, -1.0f, 1.0f);
            }

            return mono;
        }
        finally
        {
            toolchain.DeleteInstance(dsp);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        toolchain.DeleteFactory(factory);
        disposed = true;
    }
}

public sealed class AquaSynthPatchCompiler : IDisposable
{
    private readonly AquaSynthNativeOptions options;
    private AquaSynthNativeCompiler? compiler;
    private string? loadError;
    private bool disposed;

    public AquaSynthPatchCompiler(AquaSynthNativeOptions? options = null)
    {
        this.options = options ?? new AquaSynthNativeOptions();
    }

    public string? LoadError => loadError;

    public bool IsReady => compiler is not null;

    public string? FaustVersion => compiler?.FaustVersion;

    public bool TryCompileScript(AquaSynthCompileIdentity identity, out AquaSynthCompiledPatch? patch, out string? error)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        patch = null;
        if (!EnsureCompiler(out error))
        {
            return false;
        }

        try
        {
            patch = compiler!.CompileScript(identity);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryCompileSource(
        AquaSynthCompileIdentity identity,
        string source,
        float durationSeconds,
        out AquaSynthCompiledPatch? patch,
        out string? error)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        patch = null;
        if (!EnsureCompiler(out error))
        {
            return false;
        }

        try
        {
            patch = compiler!.CompileSource(
                identity,
                AquaSynthNativeCompiler.SafeFaustName(identity.FaustName),
                source,
                durationSeconds);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryRenderScript(string name, string script, float gain, out float[] samples, out string? error)
    {
        samples = [];
        if (!TryCompileScript(new AquaSynthCompileIdentity(name, name, script), out var patch, out error))
        {
            return false;
        }

        using (patch)
        {
            samples = patch!.Render(gain);
            error = null;
            return true;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        compiler?.Dispose();
        disposed = true;
    }

    private bool EnsureCompiler(out string? error)
    {
        if (compiler is not null)
        {
            error = null;
            return true;
        }

        if (loadError is not null)
        {
            error = loadError;
            return false;
        }

        if (AquaSynthNativeCompiler.TryLoad(out var loaded, out loadError, options))
        {
            compiler = loaded;
            error = null;
            return true;
        }

        error = loadError;
        return false;
    }
}

public sealed class AquaSynthNativeCompiler : IDisposable
{
    private readonly FaustNativeToolchain toolchain;
    private readonly AquaSynthNativeOptions options;
    private bool disposed;

    private AquaSynthNativeCompiler(FaustNativeToolchain toolchain, AquaSynthNativeOptions options)
    {
        this.toolchain = toolchain;
        this.options = options;
    }

    public string FaustVersion => toolchain.Version;

    public string FaustHome => toolchain.Home;

    public static bool TryLoad(out AquaSynthNativeCompiler? compiler, out string? error, AquaSynthNativeOptions? options = null)
    {
        options ??= new AquaSynthNativeOptions();
        compiler = null;
        if (!FaustNativeToolchain.TryLoad(options.FaustHome, out var toolchain, out error))
        {
            return false;
        }

        compiler = new AquaSynthNativeCompiler(toolchain!, options);
        return true;
    }

    public static bool TryRenderScript(
        string name,
        string script,
        float gain,
        out float[] samples,
        out string? error,
        AquaSynthNativeOptions? options = null)
    {
        using var session = new AquaSynthRenderSession(options);
        return session.TryRenderScript(name, script, gain, out samples, out error);
    }

    public AquaSynthCompiledPatch CompileScript(AquaSynthCompileIdentity identity)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var patch = PatchScript.Parse(identity.Script);
        var faustName = SafeFaustName(identity.FaustName);
        var export = FaustEmitter.Emit(patch, new FaustExportOptions(faustName));
        var duration = EstimateDuration(patch, options);
        var sourcePath = WriteDspSource(identity.Id, export.Source, options.DspSourceDirectory);
        return CompileSource(identity, faustName, export.Source, duration, sourcePath);
    }

    public AquaSynthCompiledPatch CompileSource(
        AquaSynthCompileIdentity identity,
        string faustName,
        string source,
        float durationSeconds,
        string? dspSourcePath = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var stopwatch = Stopwatch.StartNew();
        using var args = new NativeStringArray(["-I", toolchain.SharePath, "-single", "-ftz", "2", "-vec", "-lv", "1"]);
        using var nativeName = new NativeUtf8String(faustName);
        using var nativeSource = new NativeUtf8String(source);
        using var target = new NativeUtf8String("");
        var errorBuffer = Marshal.AllocHGlobal(FaustNativeToolchain.ErrorBufferBytes);
        try
        {
            Span<byte> empty = stackalloc byte[FaustNativeToolchain.ErrorBufferBytes];
            Marshal.Copy(empty.ToArray(), 0, errorBuffer, FaustNativeToolchain.ErrorBufferBytes);
            var factory = toolchain.CreateFactoryFromString(
                nativeName.Pointer,
                nativeSource.Pointer,
                args.Count,
                args.Pointer,
                target.Pointer,
                errorBuffer,
                -1);
            if (factory == IntPtr.Zero)
            {
                var message = Marshal.PtrToStringUTF8(errorBuffer) ?? "unknown Faust compile failure";
                throw new InvalidOperationException(message.Trim());
            }

            var probe = toolchain.CreateInstance(factory);
            if (probe == IntPtr.Zero)
            {
                toolchain.DeleteFactory(factory);
                throw new InvalidOperationException("Faust compiled but failed to create a DSP instance.");
            }

            try
            {
                var outputs = Math.Max(toolchain.GetNumOutputs(probe), 1);
                var frames = Math.Max(1, (int)MathF.Ceiling(durationSeconds * options.SampleRate));
                var controlPaths = toolchain.ControlPaths(probe);
                stopwatch.Stop();
                var manifest = new AquaSynthNativeManifest(
                    identity.Id,
                    faustName,
                    CompileKey(identity),
                    identity.Revision,
                    options.SampleRate,
                    outputs,
                    frames,
                    durationSeconds,
                    stopwatch.Elapsed.TotalMilliseconds,
                    toolchain.Version,
                    toolchain.Home,
                    dspSourcePath);
                return new AquaSynthCompiledPatch(toolchain, factory, manifest, controlPaths);
            }
            finally
            {
                toolchain.DeleteInstance(probe);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(errorBuffer);
        }
    }

    public static string CompileKey(AquaSynthCompileIdentity identity)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{identity.Revision}\n{identity.FaustName}\n{identity.Script}"));
        return Convert.ToHexString(bytes);
    }

    public static string SafeFaustName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var name = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(name) ? "aquasynth_patch" : name;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        toolchain.Dispose();
        disposed = true;
    }

    private static float EstimateDuration(SynthPatch patch, AquaSynthNativeOptions options)
    {
        var duration = patch.Voices.Count == 0
            ? options.MinRenderSeconds
            : patch.Voices.Max(voice => voice.Envelope.DurationSeconds + 0.08f);
        return Math.Clamp(duration, options.MinRenderSeconds, options.MaxRenderSeconds);
    }

    private static string? WriteDspSource(string patchId, string source, string? outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            return null;
        }

        Directory.CreateDirectory(outputRoot);
        var path = Path.Combine(outputRoot, $"{SafeFileName(patchId)}.dsp");
        File.WriteAllText(path, source);
        return path;
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var name = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(name) ? "aquasynth_patch" : name;
    }
}

public static class AquaSynthFaustToolchain
{
    public static string? Find(string? preferredHome = null)
    {
        var candidates = new[]
        {
            preferredHome,
            Environment.GetEnvironmentVariable("AQUASYNTH_FAUST_HOME"),
            Environment.GetEnvironmentVariable("AQUARIUM_FAUST_HOME"),
            Path.Combine(AppContext.BaseDirectory, "Tools", "Faust"),
            @"C:\Program Files\Faust"
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var root = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(root, "lib", "faust.dll")) &&
                Directory.Exists(Path.Combine(root, "share", "faust")))
            {
                return root;
            }
        }

        return null;
    }
}

internal sealed class FaustNativeToolchain : IDisposable
{
    public const int ErrorBufferBytes = 4096;

    private readonly IntPtr library;
    private readonly DeleteFactoryFn deleteFactory;
    private readonly CreateInstanceFn createInstance;
    private readonly DeleteInstanceFn deleteInstance;
    private readonly InitInstanceFn initInstance;
    private readonly ComputeInstanceFn computeInstance;
    private readonly GetNumOutputsFn getNumOutputs;
    private readonly BuildUserInterfaceFn buildUserInterface;
    private readonly StartFactoriesFn startFactories;
    private readonly StopFactoriesFn stopFactories;
    private bool disposed;

    private FaustNativeToolchain(IntPtr library, string home, string version, string sharePath)
    {
        this.library = library;
        Home = home;
        Version = version;
        SharePath = sharePath;
        CreateFactoryFromString = Export<CreateFactoryFromStringFn>("createCDSPFactoryFromString");
        deleteFactory = Export<DeleteFactoryFn>("deleteCDSPFactory");
        createInstance = Export<CreateInstanceFn>("createCDSPInstance");
        deleteInstance = Export<DeleteInstanceFn>("deleteCDSPInstance");
        initInstance = Export<InitInstanceFn>("initCDSPInstance");
        computeInstance = Export<ComputeInstanceFn>("computeCDSPInstance");
        getNumOutputs = Export<GetNumOutputsFn>("getNumOutputsCDSPInstance");
        buildUserInterface = Export<BuildUserInterfaceFn>("buildUserInterfaceCDSPInstance");
        startFactories = Export<StartFactoriesFn>("startMTDSPFactories");
        stopFactories = Export<StopFactoriesFn>("stopMTDSPFactories");
        _ = startFactories();
    }

    public string Home { get; }

    public string Version { get; }

    public string SharePath { get; }

    public CreateFactoryFromStringFn CreateFactoryFromString { get; }

    public static bool TryLoad(string? preferredHome, out FaustNativeToolchain? toolchain, out string? error)
    {
        toolchain = null;
        error = null;
        var home = AquaSynthFaustToolchain.Find(preferredHome);
        if (home is null)
        {
            error = "Faust toolchain not found. Expected Tools\\Faust beside the app, AQUASYNTH_FAUST_HOME, AQUARIUM_FAUST_HOME, or C:\\Program Files\\Faust.";
            return false;
        }

        var dllPath = Path.Combine(home, "lib", "faust.dll");
        if (!File.Exists(dllPath))
        {
            error = $"Faust DLL not found at {dllPath}.";
            return false;
        }

        var binPath = Path.Combine(home, "bin");
        var libPath = Path.Combine(home, "lib");
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        Environment.SetEnvironmentVariable("PATH", $"{libPath};{binPath};{path}");

        try
        {
            var library = NativeLibrary.Load(dllPath);
            var getVersion = Marshal.GetDelegateForFunctionPointer<GetVersion>(NativeLibrary.GetExport(library, "getCLibFaustVersion"));
            var version = Marshal.PtrToStringUTF8(getVersion()) ?? "unknown";
            toolchain = new FaustNativeToolchain(library, home, version, Path.Combine(home, "share", "faust"));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public IntPtr CreateInstance(IntPtr factory)
    {
        var dsp = createInstance(factory);
        if (dsp == IntPtr.Zero)
        {
            throw new InvalidOperationException("failed to create Faust DSP instance");
        }

        return dsp;
    }

    public void DeleteFactory(IntPtr factory) => _ = deleteFactory(factory);

    public void DeleteInstance(IntPtr dsp) => deleteInstance(dsp);

    public void InitInstance(IntPtr dsp, int sampleRate) => initInstance(dsp, sampleRate);

    public void Compute(IntPtr dsp, int count, IntPtr inputs, IntPtr outputs) => computeInstance(dsp, count, inputs, outputs);

    public int GetNumOutputs(IntPtr dsp) => getNumOutputs(dsp);

    public IReadOnlyList<string> ControlPaths(IntPtr dsp) =>
        BuildControlMap(dsp).Keys.Order(StringComparer.Ordinal).ToArray();

    public void SetControls(IntPtr dsp, IReadOnlyDictionary<string, float> controls)
    {
        var map = BuildControlMap(dsp);
        foreach (var (path, value) in controls)
        {
            if (!map.TryGetValue(path, out var zone) &&
                !map.TryGetValue(path.TrimStart('/'), out zone))
            {
                throw new ArgumentException($"Faust control `{path}` was not exported by the compiled patch.", nameof(controls));
            }

            Marshal.Copy([value], 0, zone, 1);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        stopFactories();
        NativeLibrary.Free(library);
        disposed = true;
    }

    private T Export<T>(string name)
        where T : Delegate
    {
        return Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    }

    private Dictionary<string, IntPtr> BuildControlMap(IntPtr dsp)
    {
        var collector = new FaustControlCollector();
        var handle = GCHandle.Alloc(collector);
        try
        {
            var glue = FaustUiGlue.Create(GCHandle.ToIntPtr(handle));
            buildUserInterface(dsp, ref glue);
            return collector.Zones;
        }
        finally
        {
            handle.Free();
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetVersion();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr CreateFactoryFromStringFn(IntPtr name, IntPtr content, int argc, IntPtr argv, IntPtr target, IntPtr error, int optLevel);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool DeleteFactoryFn(IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CreateInstanceFn(IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DeleteInstanceFn(IntPtr dsp);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void InitInstanceFn(IntPtr dsp, int sampleRate);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ComputeInstanceFn(IntPtr dsp, int count, IntPtr inputs, IntPtr outputs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetNumOutputsFn(IntPtr dsp);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BuildUserInterfaceFn(IntPtr dsp, ref FaustUiGlue glue);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool StartFactoriesFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StopFactoriesFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OpenBoxFn(IntPtr ui, IntPtr label);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CloseBoxFn(IntPtr ui);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AddButtonFn(IntPtr ui, IntPtr label, IntPtr zone);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AddSliderFn(IntPtr ui, IntPtr label, IntPtr zone, float init, float min, float max, float step);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AddBargraphFn(IntPtr ui, IntPtr label, IntPtr zone, float min, float max);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AddSoundfileFn(IntPtr ui, IntPtr label, IntPtr url, IntPtr sfZone);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DeclareFn(IntPtr ui, IntPtr zone, IntPtr key, IntPtr value);

    [StructLayout(LayoutKind.Sequential)]
    private struct FaustUiGlue
    {
        public IntPtr UiInterface;
        public OpenBoxFn OpenTabBox;
        public OpenBoxFn OpenHorizontalBox;
        public OpenBoxFn OpenVerticalBox;
        public CloseBoxFn CloseBox;
        public AddButtonFn AddButton;
        public AddButtonFn AddCheckButton;
        public AddSliderFn AddVerticalSlider;
        public AddSliderFn AddHorizontalSlider;
        public AddSliderFn AddNumEntry;
        public AddBargraphFn AddHorizontalBargraph;
        public AddBargraphFn AddVerticalBargraph;
        public AddSoundfileFn AddSoundfile;
        public DeclareFn Declare;

        public static FaustUiGlue Create(IntPtr ui) =>
            new()
            {
                UiInterface = ui,
                OpenTabBox = NoOpenBox,
                OpenHorizontalBox = NoOpenBox,
                OpenVerticalBox = NoOpenBox,
                CloseBox = NoCloseBox,
                AddButton = AddControl,
                AddCheckButton = AddControl,
                AddVerticalSlider = AddControl,
                AddHorizontalSlider = AddControl,
                AddNumEntry = AddControl,
                AddHorizontalBargraph = NoBargraph,
                AddVerticalBargraph = NoBargraph,
                AddSoundfile = NoSoundfile,
                Declare = NoDeclare
            };
    }

    private sealed class FaustControlCollector
    {
        public Dictionary<string, IntPtr> Zones { get; } = new(StringComparer.Ordinal);

        public void Add(IntPtr label, IntPtr zone)
        {
            var path = Marshal.PtrToStringUTF8(label);
            if (!string.IsNullOrWhiteSpace(path))
            {
                Zones[path] = zone;
            }
        }
    }

    private static FaustControlCollector Collector(IntPtr ui) =>
        (FaustControlCollector)GCHandle.FromIntPtr(ui).Target!;

    private static void NoOpenBox(IntPtr ui, IntPtr label) { }
    private static void NoCloseBox(IntPtr ui) { }
    private static void AddControl(IntPtr ui, IntPtr label, IntPtr zone) => Collector(ui).Add(label, zone);
    private static void AddControl(IntPtr ui, IntPtr label, IntPtr zone, float init, float min, float max, float step) => Collector(ui).Add(label, zone);
    private static void NoBargraph(IntPtr ui, IntPtr label, IntPtr zone, float min, float max) { }
    private static void NoSoundfile(IntPtr ui, IntPtr label, IntPtr url, IntPtr sfZone) { }
    private static void NoDeclare(IntPtr ui, IntPtr zone, IntPtr key, IntPtr value) { }
}

internal sealed class NativeUtf8String : IDisposable
{
    public NativeUtf8String(string value)
    {
        Pointer = Marshal.StringToCoTaskMemUTF8(value);
    }

    public IntPtr Pointer { get; }

    public void Dispose()
    {
        if (Pointer != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(Pointer);
        }
    }
}

public sealed class AquaSynthRenderSession : IDisposable
{
    private readonly AquaSynthPatchCompiler compiler;
    private bool disposed;

    public AquaSynthRenderSession(AquaSynthNativeOptions? options = null)
    {
        compiler = new AquaSynthPatchCompiler(options);
    }

    public string? LoadError => compiler.LoadError;

    public bool IsReady => compiler.IsReady;

    public string? FaustVersion => compiler.FaustVersion;

    public bool TryCompileScript(AquaSynthCompileIdentity identity, out AquaSynthCompiledPatch? patch, out string? error)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return compiler.TryCompileScript(identity, out patch, out error);
    }

    public bool TryRenderScript(string name, string script, float gain, out float[] samples, out string? error)
    {
        samples = [];
        if (!TryCompileScript(new AquaSynthCompileIdentity(name, name, script), out var patch, out error))
        {
            return false;
        }

        using (patch)
        {
            samples = patch!.Render(gain);
            error = null;
            return true;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        compiler.Dispose();
        disposed = true;
    }
}

internal sealed class NativeStringArray : IDisposable
{
    private readonly NativeUtf8String[] strings;
    private readonly GCHandle handle;

    public NativeStringArray(IReadOnlyList<string> values)
    {
        strings = values.Select(value => new NativeUtf8String(value)).ToArray();
        var pointers = strings.Select(value => value.Pointer).ToArray();
        Count = pointers.Length;
        handle = GCHandle.Alloc(pointers, GCHandleType.Pinned);
        Pointer = handle.AddrOfPinnedObject();
    }

    public int Count { get; }

    public IntPtr Pointer { get; }

    public void Dispose()
    {
        if (handle.IsAllocated)
        {
            handle.Free();
        }

        foreach (var value in strings)
        {
            value.Dispose();
        }
    }
}
