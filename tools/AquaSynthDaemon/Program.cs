using System.Text.Json;
using System.Text.Json.Serialization;
using AquaSynth.Faust;

var result = await AquaSynthDaemonCli.RunAsync(args, Console.In, Console.Out, Console.Error);
return result;

internal static class AquaSynthDaemonCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static async Task<int> RunAsync(string[] args, TextReader input, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            await WriteUsageAsync(output).ConfigureAwait(false);
            return 0;
        }

        var mode = args[0].ToLowerInvariant();
        var options = ParseOptions(args.Skip(1).ToArray());
        var storeRoot = Value(options, "--store", Path.Combine(Environment.CurrentDirectory, ".aquasynth"));
        var serviceOptions = new AquaSynthDaemonOptions(
            storeRoot,
            SampleRate: IntValue(options, "--sample-rate", 44100),
            MinRenderSeconds: FloatValue(options, "--min-seconds", 0.05f),
            MaxRenderSeconds: FloatValue(options, "--max-seconds", 3.0f));

        using var service = new AquaSynthDaemonService(serviceOptions);
        return mode switch
        {
            "once" => await RunOnceAsync(service, options, output).ConfigureAwait(false),
            "daemon" => await RunDaemonAsync(service, input, output, error).ConfigureAwait(false),
            _ => await UnknownModeAsync(mode, error).ConfigureAwait(false)
        };
    }

    private static async Task<int> RunOnceAsync(
        AquaSynthDaemonService service,
        IReadOnlyDictionary<string, string> options,
        TextWriter output)
    {
        var script = options.TryGetValue("--script", out var inlineScript)
            ? inlineScript
            : await File.ReadAllTextAsync(Value(options, "--script-file", "")).ConfigureAwait(false);

        var command = new AquaSynthInstrumentSampleCommand(
            Value(options, "--command-id", $"cli-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}"),
            Value(options, "--patch-id", "cli.patch"),
            Value(options, "--faust-name", "aquasynth_cli_patch"),
            script,
            FloatValue(options, "--duration", 0.25f),
            FloatValue(options, "--gain", 1.0f),
            ParseControls(Value(options, "--control", "")),
            IntValue(options, "--revision", 1));

        var result = await service.SampleAsync(command).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions)).ConfigureAwait(false);
        return string.Equals(result.RenderReceipt.Status, "succeeded", StringComparison.Ordinal) ? 0 : 2;
    }

    private static async Task<int> RunDaemonAsync(
        AquaSynthDaemonService service,
        TextReader input,
        TextWriter output,
        TextWriter error)
    {
        string? line;
        while ((line = await input.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                line = line.TrimStart('\uFEFF').Trim();
                var envelope = JsonSerializer.Deserialize<DaemonCommandEnvelope>(line, JsonOptions)
                    ?? throw new InvalidOperationException("Command line did not contain a JSON object.");
                if (string.Equals(envelope.Command, "patch.compile", StringComparison.OrdinalIgnoreCase))
                {
                    var command = envelope.Payload.Deserialize<AquaSynthPatchCompileCommand>(JsonOptions)
                        ?? throw new InvalidOperationException("patch.compile payload was empty.");
                    var receipt = await service.CompileAsync(command).ConfigureAwait(false);
                    await output.WriteLineAsync(JsonSerializer.Serialize(receipt, JsonOptions)).ConfigureAwait(false);
                }
                else if (string.Equals(envelope.Command, "instrument.sample", StringComparison.OrdinalIgnoreCase))
                {
                    var command = envelope.Payload.Deserialize<AquaSynthInstrumentSampleCommand>(JsonOptions)
                        ?? throw new InvalidOperationException("instrument.sample payload was empty.");
                    var receipt = await service.SampleAsync(command).ConfigureAwait(false);
                    await output.WriteLineAsync(JsonSerializer.Serialize(receipt, JsonOptions)).ConfigureAwait(false);
                }
                else
                {
                    throw new InvalidOperationException($"Unknown command '{envelope.Command}'.");
                }
            }
            catch (Exception ex)
            {
                await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
                await output.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    status = "failed",
                    failureCode = "command_failed",
                    failureMessage = ex.Message
                }, JsonOptions)).ConfigureAwait(false);
            }
        }

        return 0;
    }

    private static async Task<int> UnknownModeAsync(string mode, TextWriter error)
    {
        await error.WriteLineAsync($"Unknown AquaSynth daemon mode '{mode}'.").ConfigureAwait(false);
        return 64;
    }

    private static async Task WriteUsageAsync(TextWriter output)
    {
        await output.WriteLineAsync("""
            AquaSynthDaemon

            once --script-file patch.aqua [--store .aquasynth] [--duration 0.25] [--gain 1]
            daemon [--store .aquasynth]

            JSON-lines daemon commands:
            {"command":"instrument.sample","payload":{"commandId":"demo","patchId":"demo.patch","faustName":"demo","script":"voice wave=sine freq=440 gain=.2","durationSeconds":0.1}}
            """).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            options[key] = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index]
                : "true";
        }

        return options;
    }

    private static string Value(IReadOnlyDictionary<string, string> options, string key, string fallback) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static int IntValue(IReadOnlyDictionary<string, string> options, string key, int fallback) =>
        options.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    private static float FloatValue(IReadOnlyDictionary<string, string> options, string key, float fallback) =>
        options.TryGetValue(key, out var value) && float.TryParse(value, out var parsed) ? parsed : fallback;

    private static Dictionary<string, float>? ParseControls(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var controls = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = assignment.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && float.TryParse(parts[1], out var parsed))
            {
                controls[parts[0]] = parsed;
            }
        }

        return controls;
    }

    private sealed record DaemonCommandEnvelope(string Command, JsonElement Payload);
}
