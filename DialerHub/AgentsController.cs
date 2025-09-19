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
    public ActionResult<IEnumerable<AgentInfo>> GetAgents() => Ok(_reg.List());

    [HttpPost("{id}/command")]
    public async Task<IActionResult> SendCommand(string id, [FromBody] CommandRequest cmd)
    {
        if (!Request.Headers.TryGetValue("X-Admin-Key", out var key) || key != _cfg["AdminApiKey"]) return Unauthorized();
        if (!_reg.TryGetGroup(id, out var group)) return NotFound();
        await _hub.Clients.Group(group).SendAsync("Command", cmd);
        return Accepted(new { id, cmd.Type });
    }
}
