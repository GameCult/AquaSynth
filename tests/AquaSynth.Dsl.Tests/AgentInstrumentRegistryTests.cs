using AquaSynth.Dings;
using AquaSynthDingsMcp;

namespace AquaSynth.Dsl.Tests;

public sealed class AgentInstrumentRegistryTests
{
    [Fact]
    public void Automatic_assignment_is_stable_for_an_agent_session()
    {
        var registry = new AgentInstrumentRegistry();

        var first = registry.GetOrAssign("agent-session-one");
        var second = registry.GetOrAssign("agent-session-one");

        Assert.Equal(first, second);
        Assert.Contains(first, DingCatalog.Instruments.Keys);
    }

    [Fact]
    public void Agent_can_claim_its_own_instrument()
    {
        var registry = new AgentInstrumentRegistry();
        registry.GetOrAssign("agent-session-one");

        var claimed = registry.Claim("agent-session-one", "glass-chime");

        Assert.Equal("glass-chime", claimed);
        Assert.Equal("glass-chime", registry.GetOrAssign("agent-session-one"));
    }

    [Fact]
    public void Unknown_instrument_cannot_be_claimed()
    {
        var registry = new AgentInstrumentRegistry();

        Assert.Throws<ArgumentException>(() => registry.Claim("agent-session-one", "kazoo-of-regret"));
    }
}
