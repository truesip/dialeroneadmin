using Microsoft.AspNetCore.SignalR;

namespace DialerHub;

public class AgentHub : Hub
{
    private readonly AgentRegistry _reg;
    private readonly IConfiguration _cfg;
    private readonly AgentRepository _agents;
    private readonly string _instanceId;
    public AgentHub(AgentRegistry reg, IConfiguration cfg, AgentRepository agents, HostInstance host)
    {
        _reg = reg; _cfg = cfg; _agents = agents; _instanceId = host.InstanceId;
    }

    public async Task Register(AgentHello hello)
    {
        // Minimal token check (MVP)
        var token = _cfg["Agent:Token"];
        if (!string.IsNullOrEmpty(token) && !string.Equals(token, hello.Token, StringComparison.Ordinal))
            throw new HubException("Unauthorized agent");
        _reg.Upsert(hello.AgentId, Context.ConnectionId, hello);
        await _agents.UpsertAgentAsync(hello, _instanceId);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"agent:{hello.AgentId}");
    }

    public async Task Heartbeat(AgentHeartbeat hb)
    {
        _reg.UpdateHeartbeat(hb.AgentId, hb);
        await _agents.UpdateHeartbeatAsync(hb.AgentId, hb.Status);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // If we know the agent for this connection, mark it as disconnected only if owned by this instance
        var agentId = _reg.GetAgentIdByConnection(Context.ConnectionId);
        if (!string.IsNullOrEmpty(agentId))
        {
            await _agents.MarkDisconnectedIfOwnedAsync(agentId, _instanceId);
        }
        _reg.MarkDisconnected(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
