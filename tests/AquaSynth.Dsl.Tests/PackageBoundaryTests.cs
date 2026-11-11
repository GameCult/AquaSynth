using System.Diagnostics;
using System.IO.Compression;

namespace AquaSynth.Dsl.Tests;

public sealed class PackageBoundaryTests
{
    [Fact]
    public async Task CoreNuGetPackageContainsOnlyPublishedAuthoringSurface()
    {
        var root = RepositoryRoot();
        var outputDir = Path.Combine(Path.GetTempPath(), $"aquasynth-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);
        try
        {
            var result = await RunAsync(
                "dotnet",
                ["pack", Path.Combine(root, "src", "AquaSynth.Core", "AquaSynth.Core.csproj"), "-c", "Release", "--no-restore", "-o", outputDir, .. QuietBuildServers]);
            Assert.Equal(0, result.ExitCode);

            var package = Assert.Single(Directory.GetFiles(outputDir, "AquaSynth.Core.*.nupkg"));
            using var archive = ZipFile.OpenRead(package);
            var entries = archive.Entries.Select(entry => entry.FullName).ToArray();

            Assert.Contains(entries, entry => entry.StartsWith("lib/net10.0/AquaSynth.Core.dll", StringComparison.Ordinal));
            Assert.DoesNotContain(entries, entry => entry.StartsWith("lib/net10.0/AquaSynth.Faust.dll", StringComparison.Ordinal));
            Assert.Contains("README.md", entries);
            Assert.Contains("icon-package.png", entries);
            AssertPublishedPackagesKeepReferenceFixturesOut(entries);
        }
        finally
        {
            Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task FaustNuGetPackageContainsRuntimeSurfaceAndDependsOnCore()
    {
        var root = RepositoryRoot();
        var outputDir = Path.Combine(Path.GetTempPath(), $"aquasynth-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);
        try
        {
            var result = await RunAsync(
                "dotnet",
                ["pack", Path.Combine(root, "src", "AquaSynth.Faust", "AquaSynth.Faust.csproj"), "-c", "Release", "--no-restore", "-o", outputDir, .. QuietBuildServers]);
            Assert.Equal(0, result.ExitCode);

            var package = Assert.Single(Directory.GetFiles(outputDir, "AquaSynth.Faust.*.nupkg"));
            using var archive = ZipFile.OpenRead(package);
            var entries = archive.Entries.Select(entry => entry.FullName).ToArray();

            Assert.Contains(entries, entry => entry.StartsWith("lib/net10.0/AquaSynth.Faust.dll", StringComparison.Ordinal));
            Assert.Contains(entries, entry => entry.EndsWith(".nuspec", StringComparison.Ordinal));
            Assert.Contains("AquaSynth.Core", ReadNuspecText(archive));
            Assert.Contains("README.md", entries);
            Assert.Contains("icon-package.png", entries);
            AssertPublishedPackagesKeepReferenceFixturesOut(entries);
        }
        finally
        {
            Directory.Delete(outputDir, recursive: true);
        }
    }

    private static string ReadNuspecText(ZipArchive archive)
    {
        var nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using var stream = nuspec.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void AssertPublishedPackagesKeepReferenceFixturesOut(IReadOnlyCollection<string> entries)
    {
            Assert.DoesNotContain(entries, entry => entry.StartsWith("tests/", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(entries, entry => entry.StartsWith("Fixtures/", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(entries, entry => entry.StartsWith("patches/", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(entries, entry => entry.EndsWith(".aqua", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(entries, entry => entry.EndsWith(".syx", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(entries, entry => entry.EndsWith(".xiz", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(entries, entry => entry.EndsWith(".py", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(entries, entry => entry.StartsWith("external/", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(entries, entry => entry.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(entries, entry => entry.EndsWith(".h", StringComparison.OrdinalIgnoreCase));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AquaSynth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("could not find repository root");
    }

    // `dotnet pack` leaves VBCSCompiler and MSBuild node-reuse processes running. They inherit our redirected stdout/stderr pipes, so
    // ReadToEndAsync never sees EOF and the test hangs after pack has already written the nupkg (the 2026-11-11 hang). Pack without
    // them, and bound every wait so a regression fails with a message instead of hanging.
    private static readonly string[] QuietBuildServers = ["-p:UseSharedCompilation=false", "-nodeReuse:false", "--disable-build-servers"];
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PipeDrainGrace = TimeSpan.FromSeconds(10);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"failed to start `{fileName}`");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"`{fileName} {string.Join(' ', arguments)}` did not exit within {ProcessTimeout}.");
        }

        var drained = Task.WhenAll(stdout, stderr);
        if (await Task.WhenAny(drained, Task.Delay(PipeDrainGrace)) != drained)
        {
            throw new TimeoutException(
                $"`{fileName}` exited but its output pipes stayed open for {PipeDrainGrace}: a child it spawned (build server?) inherited them.");
        }

        return (process.ExitCode, await stdout, await stderr);
    }
}
