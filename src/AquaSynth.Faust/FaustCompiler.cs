using System.Diagnostics;
using System.Globalization;

using AquaSynth.Dsl;

namespace AquaSynth.Faust;

public enum FaustTargetLanguage
{
    C,
    Cpp,
    CSharp,
    Rust,
    WebAssembly,
    WebAssemblyText
}

public sealed record FaustCompileOptions(FaustTargetLanguage Language, string OutputPath);

public sealed record FaustValidation(string Command, bool Success, int? StatusCode, string Stdout, string Stderr);

public sealed record FaustWebAssemblyCompileOptions(
    string OutputDirectory,
    string BaseName = "aquasynth_patch",
    bool Stereo = false);

public sealed record FaustWebAssemblyManifest(
    string Name,
    string DspPath,
    string WasmPath,
    string JsonPath,
    string FaustPath,
    bool Success,
    int? StatusCode,
    string Stdout,
    string Stderr);

public sealed record FaustRenderOptions(int SampleRate = 44100, float DurationSeconds = 1);

public sealed record FaustRender(float[] Samples, int SampleRate, string Command, string Stdout, string Stderr);

public static class FaustCompiler
{
    public static string? FindFaust()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var names = OperatingSystem.IsWindows() ? new[] { "faust.exe", "faust" } : ["faust"];
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        var windowsDefault = @"C:\Program Files\Faust\bin\faust.exe";
        return File.Exists(windowsDefault) ? windowsDefault : null;
    }

    public static async Task<FaustValidation?> CompileAsync(
        string source,
        FaustCompileOptions options,
        string? faustPath = null,
        CancellationToken cancellationToken = default)
    {
        faustPath ??= FindFaust();
        if (faustPath is null) return null;

        var sourcePath = Path.Combine(Path.GetTempPath(), $"aquasynth-{Guid.NewGuid():N}.dsp");
        await File.WriteAllTextAsync(sourcePath, source, cancellationToken);
        try
        {
            var result = await RunAsync(
                faustPath,
                ["-lang", Language(options.Language), "-o", options.OutputPath, sourcePath],
                cancellationToken);
            return new FaustValidation(faustPath, result.ExitCode == 0, result.ExitCode, result.Stdout, result.Stderr);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    public static async Task<FaustWebAssemblyManifest?> CompileWebAssemblyAsync(
        SynthPatch patch,
        FaustWebAssemblyCompileOptions options,
        string? faustPath = null,
        CancellationToken cancellationToken = default)
    {
        var export = FaustEmitter.Emit(patch, new FaustExportOptions(options.BaseName, options.Stereo));
        return await CompileWebAssemblySourceAsync(export.Source, options, faustPath, cancellationToken);
    }

    public static async Task<FaustWebAssemblyManifest?> CompileWebAssemblyScriptAsync(
        string script,
        FaustWebAssemblyCompileOptions options,
        string? faustPath = null,
        CancellationToken cancellationToken = default)
    {
        var export = FaustEmitter.EmitScript(script, new FaustExportOptions(options.BaseName, options.Stereo));
        return await CompileWebAssemblySourceAsync(export.Source, options, faustPath, cancellationToken);
    }

    public static async Task<FaustWebAssemblyManifest?> CompileWebAssemblySourceAsync(
        string source,
        FaustWebAssemblyCompileOptions options,
        string? faustPath = null,
        CancellationToken cancellationToken = default)
    {
        faustPath ??= FindFaust();
        if (faustPath is null) return null;

        Directory.CreateDirectory(options.OutputDirectory);
        var baseName = SafeArtifactName(options.BaseName);
        var dspPath = Path.Combine(options.OutputDirectory, $"{baseName}.dsp");
        var wasmPath = Path.Combine(options.OutputDirectory, $"{baseName}.wasm");
        var jsonPath = Path.Combine(options.OutputDirectory, $"{baseName}.json");
        await File.WriteAllTextAsync(dspPath, source, cancellationToken);

        var result = await RunAsync(
            faustPath,
            [
                "-lang", Language(FaustTargetLanguage.WebAssembly),
                "-json",
                "-o", wasmPath,
                dspPath
            ],
            cancellationToken);

        return new FaustWebAssemblyManifest(
            options.BaseName,
            dspPath,
            wasmPath,
            jsonPath,
            faustPath,
            result.ExitCode == 0 && File.Exists(wasmPath),
            result.ExitCode,
            result.Stdout,
            result.Stderr);
    }

    public static async Task<FaustValidation?> ValidateAsync(
        string source,
        string? faustPath = null,
        CancellationToken cancellationToken = default)
    {
        var output = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        return await CompileAsync(source, new FaustCompileOptions(FaustTargetLanguage.Cpp, output), faustPath, cancellationToken);
    }

    public static async Task<FaustRender?> RenderAsync(
        string source,
        FaustRenderOptions? options = null,
        string? faustPath = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new FaustRenderOptions();
        faustPath ??= FindFaust();
        if (faustPath is null) return null;

        var frames = Math.Max(1, (int)MathF.Round(options.SampleRate * Math.Max(options.DurationSeconds, 1f / options.SampleRate)));
        var tempDir = Path.Combine(Path.GetTempPath(), $"aquasynth-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "render.dsp");
            var generatedPath = Path.Combine(tempDir, "RenderDsp.cs");
            var outputPath = Path.Combine(tempDir, "render.f32");
            await File.WriteAllTextAsync(sourcePath, source, cancellationToken);

            var faust = await RunAsync(
                faustPath,
                ["-lang", "csharp", "-double", "-cn", "RenderDsp", "-o", generatedPath, sourcePath],
                cancellationToken);
            if (faust.ExitCode != 0)
            {
                return new FaustRender([], options.SampleRate, faustPath, faust.Stdout, faust.Stderr);
            }

            var archDir = await FaustArchDirAsync(faustPath, cancellationToken);
            var basePath = archDir is null ? null : Path.Combine(archDir, "CSharpFaustBase.cs");
            if (basePath is null || !File.Exists(basePath))
            {
                return new FaustRender([], options.SampleRate, faustPath, faust.Stdout, $"could not find CSharpFaustBase.cs under Faust archdir `{archDir}`");
            }

            File.Copy(basePath, Path.Combine(tempDir, "CSharpFaustBase.cs"));
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Render.csproj"), RenderProjectSource(), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Program.cs"), RenderProgramSource(), cancellationToken);

            var dotnet = await RunAsync(
                "dotnet",
                ["run", "--project", Path.Combine(tempDir, "Render.csproj"), "--", outputPath, options.SampleRate.ToString(CultureInfo.InvariantCulture), frames.ToString(CultureInfo.InvariantCulture)],
                cancellationToken,
                DotnetRenderEnvironment(tempDir));
            if (dotnet.ExitCode != 0 || !File.Exists(outputPath))
            {
                return new FaustRender([], options.SampleRate, "dotnet", dotnet.Stdout, dotnet.Stderr);
            }

            var bytes = await File.ReadAllBytesAsync(outputPath, cancellationToken);
            var samples = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * sizeof(float));
            return new FaustRender(samples, options.SampleRate, "dotnet", dotnet.Stdout, dotnet.Stderr);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string Language(FaustTargetLanguage language) => language switch
    {
        FaustTargetLanguage.C => "c",
        FaustTargetLanguage.Cpp => "cpp",
        FaustTargetLanguage.CSharp => "csharp",
        FaustTargetLanguage.Rust => "rust",
        FaustTargetLanguage.WebAssembly => "wasm",
        FaustTargetLanguage.WebAssemblyText => "wast",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
    };

    private static string SafeArtifactName(string value)
    {
        var sanitized = new string(value.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "aquasynth_patch" : sanitized;
    }

    private static async Task<string?> FaustArchDirAsync(string faustPath, CancellationToken cancellationToken)
    {
        var result = await RunAsync(faustPath, ["-archdir"], cancellationToken);
        return result.ExitCode == 0 ? result.Stdout.Trim() : null;
    }

    private static string RenderProjectSource() =>
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;

    private static string RenderProgramSource() =>
        """
        using System.Globalization;

        var outputPath = args[0];
        var sampleRate = int.Parse(args[1], CultureInfo.InvariantCulture);
        var frames = int.Parse(args[2], CultureInfo.InvariantCulture);
        var dsp = new RenderDsp();
        dsp.Init(sampleRate);

        var inputs = new double[dsp.GetNumInputs()][];
        for (var i = 0; i < inputs.Length; i++) inputs[i] = new double[frames];

        var outputs = new double[dsp.GetNumOutputs()][];
        for (var i = 0; i < outputs.Length; i++) outputs[i] = new double[frames];

        dsp.Compute(frames, inputs, outputs);

        await using var stream = File.Create(outputPath);
        await using var writer = new BinaryWriter(stream);
        var output = outputs.Length == 0 ? Array.Empty<double>() : outputs[0];
        for (var i = 0; i < frames; i++) writer.Write((float)output[i]);
        """;

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                start.Environment[key] = value;
            }
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"failed to start `{fileName}`");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        return (process.ExitCode, await stdout, await stderr);
    }

    private static IReadOnlyDictionary<string, string> DotnetRenderEnvironment(string tempDir)
    {
        var home = Path.Combine(tempDir, "dotnet-home");
        var appData = Path.Combine(tempDir, "dotnet-appdata");
        var localAppData = Path.Combine(tempDir, "dotnet-localappdata");
        var packages = Path.Combine(tempDir, "nuget-packages");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(localAppData);
        Directory.CreateDirectory(packages);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_CLI_HOME"] = home,
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["NUGET_PACKAGES"] = packages,
            ["APPDATA"] = appData,
            ["LOCALAPPDATA"] = localAppData,
            ["USERPROFILE"] = home,
            ["HOME"] = home
        };
    }
}
