using Microsoft.AspNetCore.SignalR;

namespace DialerHub;

public class HostInstance
{
    public string InstanceId { get; }
    public HostInstance(string instanceId) { InstanceId = instanceId; }
}

public class CommandDispatcher : BackgroundService
{
    private readonly ILogger<CommandDispatcher> _log;
    private readonly CommandRepository _commands;
    private readonly IHubContext<AgentHub> _hub;
    private readonly HostInstance _host;

    public CommandDispatcher(ILogger<CommandDispatcher> log, CommandRepository commands, IHubContext<AgentHub> hub, HostInstance host)
    {
        _log = log; _commands = commands; _hub = hub; _host = host;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("CommandDispatcher running on {Instance}", _host.InstanceId);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await _commands.ClaimBatchAsync(_host.InstanceId, 50);
                foreach (var item in batch)
                {
                    try
                    {
                        var group = $"agent:{item.AgentId}";
                        var payload = new CommandRequest { Type = item.Type, Payload = string.IsNullOrEmpty(item.Payload) ? null : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(item.Payload!) };
                        await _hub.Clients.Group(group).SendAsync("Command", payload, cancellationToken: stoppingToken);
                        await _commands.MarkSentAsync(item.Id);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Failed to dispatch command {Id} to {AgentId}", item.Id, item.AgentId);
                        await _commands.MarkFailedAsync(item.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Dispatcher loop error");
            }
            await Task.Delay(500, stoppingToken);
        }
    }
}
