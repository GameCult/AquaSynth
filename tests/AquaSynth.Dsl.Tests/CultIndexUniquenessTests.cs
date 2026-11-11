using System.Reflection;
using AquaSynth.Dsl;
using GameCult.Caching;
using GameCult.Caching.MessagePack;

namespace AquaSynth.Dsl.Tests;

// CultLib #90 (7760219b): [CultIndex] means unique, enforced at commit and load; [CultRole] names a many-row selection role.
// A member that two rows can share must be a role. The only [CultIndex] left is the CommandId of a receipt: every receipt is stored
// under a key derived from its CommandId alone (AquaSynthCultNetDaemon.*ReceiptKey), so a second row with that CommandId replaces the first.
public sealed class CultIndexUniquenessTests
{
    private static readonly string[] ReceiptTypes =
    [
        nameof(AquaSynthPatchCompileReceipt), nameof(AquaSynthRenderSampleReceipt), nameof(AquaSynthAutomationStreamReceipt), nameof(AquaSynthInstrumentOpenReceipt),
        nameof(AquaSynthInstrumentControlReceipt), nameof(AquaSynthInstrumentBlockReceipt), nameof(AquaSynthInstrumentCloseReceipt),
    ];

    [Fact]
    public void TheOnlyCultIndexMembersAreReceiptCommandIds()
    {
        var indexed = new[] { typeof(AquaSynthDaemonService).Assembly, typeof(SpeechRenderResult).Assembly }.Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(member => member is PropertyInfo or FieldInfo && member.GetCustomAttribute<CultIndexAttribute>() is not null)
                .Select(member => $"{type.Name}.{member.Name}"))
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(ReceiptTypes.Select(name => $"{name}.CommandId").OrderBy(name => name, StringComparer.Ordinal), indexed);
    }

    [Fact]
    public async Task ManyRowsMaySharePatchAndSessionAcrossDaemonDocuments()
    {
        var path = TempFile();
        try
        {
            using (var cache = await CultCacheMessagePack.OpenAsync(path))
            {
                string[] paths = [];
                for (var i = 0; i < 3; i++)
                {
                    // Three commands, one patch, one session: the shape a client that opens, drives and closes one instrument produces.
                    await Put(cache, $"cmd-{i}-control", new AquaSynthInstrumentControlCommand($"cmd-{i}-control", "session-1", new Dictionary<string, float>()));
                    await Put(cache, $"cmd-{i}-block", new AquaSynthInstrumentBlockCommand($"cmd-{i}-block", "session-1"));
                    await Put(cache, $"cmd-{i}-close", new AquaSynthInstrumentCloseCommand($"cmd-{i}-close", "session-1"));
                    await Put(cache, $"sess-{i}", new AquaSynthCompiledInstrumentSession($"sess-{i}", "patch-1", $"key-{i}", "f", 1, 48000, 0, 1, 128, 0.1f, "t", paths, paths));
                    await Put(cache, $"live-{i}", new AquaSynthLiveInstrumentSessionState($"live-{i}", "patch-1", "open", "t", "t", "", 48000, 0, 1, 128, 1f, 0, new Dictionary<string, float>(), "", ""));
                    await Put(cache, $"cmd-{i}-compile", new AquaSynthPatchCompileReceipt($"cmd-{i}-compile", $"cmd-{i}", "patch-1", "session-1", "succeeded", "t", "k", 1, paths, paths));
                    await Put(cache, $"cmd-{i}-render", new AquaSynthRenderSampleReceipt($"cmd-{i}-render", $"cmd-{i}", "patch-1", "session-1", "succeeded", "t", 48000, 1, 0.1f, 0, 0, "", "", ""));
                    await Put(cache, $"cmd-{i}-stream", new AquaSynthAutomationStreamReceipt($"cmd-{i}-stream", $"cmd-{i}", "patch-1", "session-1", "succeeded", "t", "v", "a", "c", 48000, 128, 1, [], []));
                    await Put(cache, $"cmd-{i}-open", new AquaSynthInstrumentOpenReceipt($"cmd-{i}-open", $"cmd-{i}", "patch-1", "session-1", "succeeded", "t", 48000, 0, 1, 128, paths, paths));
                    await Put(cache, $"cmd-{i}-ctl", new AquaSynthInstrumentControlReceipt($"cmd-{i}-ctl", $"cmd-{i}", "session-1", "applied", "t", 0));
                    await Put(cache, $"cmd-{i}-blk", new AquaSynthInstrumentBlockReceipt($"cmd-{i}-blk", $"cmd-{i}", "session-1", "succeeded", "t", 0, 48000, 128, 0, 0, "", ""));
                    await Put(cache, $"cmd-{i}-cls", new AquaSynthInstrumentCloseReceipt($"cmd-{i}-cls", $"cmd-{i}", "session-1", "closed", "t", true));
                }

                await cache.FlushAsync();
            }

            // Reopening hydrates and revalidates: a unique index would refuse the file here.
            using var reloaded = await CultCacheMessagePack.OpenAsync(path);
            Assert.Equal(3, reloaded.GetAll<AquaSynthInstrumentBlockCommand>().Count());
            Assert.Equal(3, reloaded.GetAll<AquaSynthCompiledInstrumentSession>().Count(session => session.PatchId == "patch-1"));
            Assert.Equal(3, reloaded.GetAll<AquaSynthInstrumentOpenReceipt>().Count(receipt => receipt.SessionId == "session-1" && receipt.PatchId == "patch-1"));
            Assert.Equal(3, reloaded.GetAll<AquaSynthInstrumentBlockReceipt>().Count(receipt => receipt.SessionId == "session-1"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OneRowPerCommandIdPerReceiptType()
    {
        var path = TempFile();
        try
        {
            using var cache = await CultCacheMessagePack.OpenAsync(path);
            string[] paths = [];
            AquaSynthPatchCompileReceipt Receipt(string command, string status) =>
                new(AquaSynthCultNetDaemon.CompileReceiptKey(command).Value, command, "patch-1", "session-1", status, "t", "k", 1, paths, paths);
            foreach (var (command, status) in new[] { ("a", "failed"), ("a", "succeeded"), ("b", "succeeded") })
            {
                var receipt = Receipt(command, status);
                await cache.UpsertAsync(receipt, new CultRecordHandle<AquaSynthPatchCompileReceipt>(AquaSynthCultNetDaemon.CompileReceiptKey(command)));
            }

            // A retried command replaces its own receipt; it cannot add a second row that carries the same CommandId.
            var all = cache.GetAll<AquaSynthPatchCompileReceipt>().OrderBy(receipt => receipt.CommandId, StringComparer.Ordinal).ToArray();
            Assert.Equal(["a", "b"], all.Select(receipt => receipt.CommandId));
            Assert.Equal("succeeded", all[0].Status);
            foreach (var kind in new Func<string, CultRecordKey>[] { AquaSynthCultNetDaemon.CompileReceiptKey, AquaSynthCultNetDaemon.RenderReceiptKey, AquaSynthCultNetDaemon.StreamReceiptKey,
                         AquaSynthCultNetDaemon.OpenReceiptKey, AquaSynthCultNetDaemon.ControlReceiptKey, AquaSynthCultNetDaemon.BlockReceiptKey, AquaSynthCultNetDaemon.CloseReceiptKey })
            {
                Assert.NotEqual(kind("a"), kind("b"));
                Assert.Equal(kind("a"), kind("a"));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Task Put<T>(CultCache cache, string key, T document) where T : class =>
        cache.UpsertAsync(document, new CultRecordHandle<T>(new CultRecordKey(key)));

    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"aquasynth-cultindex-{Guid.NewGuid():N}.cc");
}
