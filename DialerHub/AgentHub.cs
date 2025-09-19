using Microsoft.AspNetCore.SignalR;

namespace DialerHub;

public class AgentHub : Hub
{
    private readonly AgentRegistry _reg;
    private readonly IConfiguration _cfg;
    public AgentHub(AgentRegistry reg, IConfiguration cfg)
    {
        _reg = reg; _cfg = cfg;
    }

    public Task Register(AgentHello hello)
    {
        // Minimal token check (MVP)
        var token = _cfg["Agent:Token"];
        if (!string.IsNullOrEmpty(token) && !string.Equals(token, hello.Token, StringComparison.Ordinal))
            throw new HubException("Unauthorized agent");
        _reg.Upsert(hello.AgentId, Context.ConnectionId, hello);
        return Groups.AddToGroupAsync(Context.ConnectionId, $"agent:{hello.AgentId}");
    }

    public Task Heartbeat(AgentHeartbeat hb)
    {
        _reg.UpdateHeartbeat(hb.AgentId, hb);
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _reg.MarkDisconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}