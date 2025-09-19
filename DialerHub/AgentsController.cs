using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace DialerHub;

[ApiController]
[Route("api/[controller]")]
public class AgentsController : ControllerBase
{
    private readonly AgentRegistry _reg;
    private readonly IHubContext<AgentHub> _hub;
    private readonly IConfiguration _cfg;
    public AgentsController(AgentRegistry reg, IHubContext<AgentHub> hub, IConfiguration cfg)
    {
        _reg = reg; _hub = hub; _cfg = cfg;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AgentInfo>>> GetAgents([FromServices] AgentRepository repo)
        => Ok(await repo.ListAsync());

    [HttpPost("{id}/command")]
    public async Task<IActionResult> SendCommand(string id, [FromBody] CommandRequest cmd)
    {
        if (!Request.Headers.TryGetValue("X-Admin-Key", out var key) || key != _cfg["AdminApiKey"]) return Unauthorized();

        // Always enqueue to DB so any instance can deliver the command
        var repo = HttpContext.RequestServices.GetRequiredService<CommandRepository>();
        var cmdId = await repo.EnqueueAsync(id, cmd);

        // Best-effort immediate send for low latency if the agent is on this instance
        var group = $"agent:{id}";
        await _hub.Clients.Group(group).SendAsync("Command", cmd);
        return Accepted(new { id, cmd.Type, cmdId });
    }
}
