namespace AquaSynth.Dsl.Tests;

public sealed class FaustLoaderPlatformTests
{
    [Fact]
    public void LibraryFileNamesFollowThePlatform()
    {
        var names = AquaSynthFaustToolchain.LibraryFileNames;
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("libfaust.dll", names);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.Equal(["libfaust.so"], names);
        }
    }

    [Fact]
    public void FindLibraryLooksUnderLibForThePlatformName()
    {
        var home = Directory.CreateTempSubdirectory("faust-home").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(home, "lib"));
            Directory.CreateDirectory(Path.Combine(home, "share", "faust"));
            Assert.Null(AquaSynthFaustToolchain.FindLibrary(home));
            Assert.Null(AquaSynthFaustToolchain.Find(home));

            var lib = Path.Combine(home, "lib", AquaSynthFaustToolchain.LibraryFileNames[0]);
            File.WriteAllBytes(lib, []);
            Assert.Equal(lib, AquaSynthFaustToolchain.FindLibrary(home));
            Assert.Equal(Path.GetFullPath(home), AquaSynthFaustToolchain.Find(home));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void ExplicitLibraryPathMustExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-dir", AquaSynthFaustToolchain.LibraryFileNames[0]);
        var loaded = AquaSynthNativeCompiler.TryLoad(
            out var compiler, out var error, new AquaSynthNativeOptions(FaustLibraryPath: missing));
        Assert.False(loaded);
        Assert.Null(compiler);
        Assert.Contains("override not found", error);
    }

    // Real load. The source uses no Faust libraries, so a prefix without share/faust/*.lib still works.
    // Set AQUASYNTH_FAUST_LIBRARY to a libfaust file; unset, this is a no-op.
    [Fact]
    public void ExplicitLibraryPathLoadsRealFaustAndCompiles()
    {
        var library = Environment.GetEnvironmentVariable(AquaSynthFaustToolchain.LibraryOverrideVariable);
        if (string.IsNullOrWhiteSpace(library))
        {
            return;
        }

        Assert.True(
            AquaSynthNativeCompiler.TryLoad(out var compiler, out var error, new AquaSynthNativeOptions(FaustLibraryPath: library)),
            error);
        using (compiler)
        {
            Console.WriteLine($"SMOKE library={Path.GetFullPath(library)} faustVersion={compiler!.FaustVersion} home={compiler.FaustHome}");
            Assert.False(string.IsNullOrWhiteSpace(compiler.FaustVersion));

            var identity = new AquaSynthCompileIdentity("smoke", "smoke", "");
            using var patch = compiler.CompileSource(identity, "smoke", "process = (+(0.0625) ~ _) : sin : *(0.1);", 0.1f);
            var samples = patch.Render();
            Console.WriteLine($"SMOKE compiled outputs={patch.Manifest.OutputCount} frames={samples.Length} peak={samples.Max(Math.Abs):F4}");
            Assert.Contains(samples, s => Math.Abs(s) > 0.01f);
        }
    }
}
