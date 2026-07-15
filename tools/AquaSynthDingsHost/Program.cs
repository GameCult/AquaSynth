using AquaSynthDingsHost;

using var singleton = new Mutex(initiallyOwned: true, "Local\\GameCult.AquaSynthDings.Playback.v1", out var ownsMutex);
if (!ownsMutex) return;

await using var host = new DingsPlaybackHost();
await host.RunAsync(CancellationToken.None);
