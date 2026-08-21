using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AquaSynth.Dings;

namespace AquaSynthDingsMcp;

public sealed class AgentInstrumentRegistry
{
    private readonly ConcurrentDictionary<string, string> assignments = new(StringComparer.Ordinal);
    private readonly string[] instrumentIds = DingCatalog.Instruments.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public string GetOrAssign(string agentSessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentSessionId);
        return assignments.GetOrAdd(agentSessionId, AssignDeterministically);
    }

    public string Claim(string agentSessionId, string preferredInstrumentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentSessionId);
        if (!DingCatalog.Instruments.TryGetValue(preferredInstrumentId, out var instrument))
        {
            throw new ArgumentException($"Unknown instrument '{preferredInstrumentId}'.");
        }

        assignments[agentSessionId] = instrument.Id;
        return instrument.Id;
    }

    private string AssignDeterministically(string agentSessionId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(agentSessionId));
        var slot = BitConverter.ToUInt32(digest, 0) % (uint)instrumentIds.Length;
        return instrumentIds[slot];
    }
}
