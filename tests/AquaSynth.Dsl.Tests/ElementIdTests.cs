using AquaSynth.Dsl;
using GameCult.Caching;
using GameCult.Caching.MessagePack;

namespace AquaSynth.Dsl.Tests;

// CultLib abd1a92e refuses to register a document whose list element type has no [CultElementId] member (CultCache.cs:1008-1010);
// opening any cache here registers every AquaSynth document type, so these tests also fail if one element type loses its id.
public sealed class ElementIdTests
{
    // Fixtures/CultCache/pre-element-ids.cc was written by the previous pin (CultLib 42dce361), before list elements had ids:
    // an automation-stream command with 2 control frames and a stream receipt with 2 stream descriptors and 2 packets.
    [Fact]
    public async Task AStoreWrittenBeforeElementIdsReopensWithStableUniqueIds()
    {
        var path = CopyFixture();
        try
        {
            string[] first;
            using (var cache = await CultCacheMessagePack.OpenAsync(path))
            {
                var command = cache.GetAll<AquaSynthAutomationStreamCommand>().Single();
                var receipt = cache.GetAll<AquaSynthAutomationStreamReceipt>().Single();
                Assert.Equal([0, 4], command.ControlFrames!.Select(frame => frame.Block));
                Assert.Equal(220f, command.ControlFrames![0].Controls["/freq"]);
                Assert.Equal(["audio-1", "control-1"], receipt.Streams.Select(stream => stream.StreamId));
                Assert.Equal([0UL, 1UL], receipt.Packets.Select(packet => packet.Sequence));
                first = Ids(command, receipt);
                Assert.All(first, id => Assert.False(string.IsNullOrEmpty(id)));
                Assert.Equal(2, command.ControlFrames!.Select(frame => frame.ElementId).Distinct().Count());
                Assert.Equal(2, receipt.Packets.Select(packet => packet.ElementId).Distinct().Count());
            }

            // A second reader of the same bytes mints the same ids (load mints from the record key and element path).
            using var again = await CultCacheMessagePack.OpenAsync(path);
            Assert.Equal(first, Ids(again.GetAll<AquaSynthAutomationStreamCommand>().Single(), again.GetAll<AquaSynthAutomationStreamReceipt>().Single()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task IdsSurviveAWriteAndReload()
    {
        var path = CopyFixture();
        try
        {
            string[] first;
            using (var cache = await CultCacheMessagePack.OpenAsync(path))
            {
                first = Ids(cache.GetAll<AquaSynthAutomationStreamCommand>().Single(), cache.GetAll<AquaSynthAutomationStreamReceipt>().Single());
                // An unrelated write rewrites the store in the current shape.
                await cache.UpsertAsync(new AquaSynthInstrumentCloseCommand("c-1", "s-1"), new CultRecordHandle<AquaSynthInstrumentCloseCommand>(new CultRecordKey("c-1")));
                await cache.FlushAsync();
            }

            using var reloaded = await CultCacheMessagePack.OpenAsync(path);
            Assert.Equal(first, Ids(reloaded.GetAll<AquaSynthAutomationStreamCommand>().Single(), reloaded.GetAll<AquaSynthAutomationStreamReceipt>().Single()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task NewWritesKeepNaturalIdsAndDeriveThePacketIdFromItsSequence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aquasynth-elementid-{Guid.NewGuid():N}.cc");
        try
        {
            using (var cache = await CultCacheMessagePack.OpenAsync(path))
            {
                var packets = new[] { new AquaSynthAutomationPacketReceipt(7, 0, 1, 128, 0.5f, 0.1f, "u", "h", "p", 0), new AquaSynthAutomationPacketReceipt(9, 1, 1, 128, 0.5f, 0.1f, "u", "h", "p", 0) };
                var streams = new[] { new AquaSynthCultMeshStreamDescriptor("audio-9", "v", "p", "audio", "f32", 48000, 1, 128, [], null) };
                await cache.UpsertAsync(new AquaSynthAutomationStreamReceipt("run", "cmd", "patch", "session", "succeeded", "t", "v", "a", "c", 48000, 128, 1, streams, packets),
                    new CultRecordHandle<AquaSynthAutomationStreamReceipt>(new CultRecordKey("run")));
                await cache.FlushAsync();
            }

            using var reloaded = await CultCacheMessagePack.OpenAsync(path);
            var receipt = reloaded.GetAll<AquaSynthAutomationStreamReceipt>().Single();
            Assert.Equal(["7", "9"], receipt.Packets.Select(packet => packet.ElementId));
            Assert.Equal("audio-9", receipt.Streams.Single().StreamId);
            Assert.Equal("audio-9", receipt.Streams.Single().ElementId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string[] Ids(AquaSynthAutomationStreamCommand command, AquaSynthAutomationStreamReceipt receipt) =>
        [.. command.ControlFrames!.Select(frame => frame.ElementId), .. receipt.Packets.Select(packet => packet.ElementId)];

    private static string CopyFixture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aquasynth-preids-{Guid.NewGuid():N}.cc");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CultCache", "pre-element-ids.cc"), path);
        return path;
    }
}
