using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace DialerHub;

public class AgentSession
{
    public required string AgentId { get; init; }
    public string ConnId { get; set; } = string.Empty;
    public string Group => $"agent:{AgentId}";
    public required AgentHello Hello { get; init; }
    public AgentHeartbeat? Heartbeat { get; set; }
    public DateTime LastHeartbeatUtc { get; set; }
}

public class AgentRegistry
{
    private readonly ConcurrentDictionary<string, AgentSession> _agents = new();

    public void Upsert(string agentId, string connId, AgentHello hello)
    {
        _agents[agentId] = new AgentSession
        {
            AgentId = agentId,
            ConnId = connId,
            Hello = hello,
            LastHeartbeatUtc = DateTime.UtcNow
        };
    }

    public void UpdateHeartbeat(string agentId, AgentHeartbeat hb)
    {
        if (_agents.TryGetValue(agentId, out var s))
        {
            s.Heartbeat = hb;
            s.LastHeartbeatUtc = DateTime.UtcNow;
        }
    }

    public void MarkDisconnected(string connId)
    {
        foreach (var kv in _agents)
        {
            if (kv.Value.ConnId == connId)
            {
                _agents.TryRemove(kv.Key, out _);
            }
        }
    }

    public string? GetAgentIdByConnection(string connId)
    {
        foreach (var kv in _agents)
        {
            if (kv.Value.ConnId == connId) return kv.Key;
        }
        return null;
    }

    public IEnumerable<AgentInfo> List() => _agents.Values.Select(v => new AgentInfo
    {
        AgentId = v.AgentId,
        MachineName = v.Hello.MachineName,
        IPs = v.Hello.IPs,
        MACs = v.Hello.MACs,
        LastSeenUtc = v.LastHeartbeatUtc,
        Status = v.Heartbeat?.Status ?? "unknown"
    });

    public bool TryGetGroup(string agentId, out string group)
    {
        if (_agents.TryGetValue(agentId, out var s))
        {
            group = s.Group;
            return true;
        }
        group = string.Empty; return false;
    }
}
