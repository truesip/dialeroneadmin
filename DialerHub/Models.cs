namespace DialerHub;

public record AgentHello
{
    public required string AgentId { get; init; }
    public string? Token { get; init; }
    public required string MachineName { get; init; }
    public required string[] IPs { get; init; }
    public required string[] MACs { get; init; }
    public string? Version { get; init; }
}

public record AgentHeartbeat
{
    public required string AgentId { get; init; }
    public string Status { get; init; } = "unknown"; // running|stopped|disabled
    public string? UserName { get; init; }
}

public record AgentInfo
{
    public required string AgentId { get; init; }
    public required string MachineName { get; init; }
    public required string[] IPs { get; init; }
    public required string[] MACs { get; init; }
    public DateTime LastSeenUtc { get; init; }
    public string Status { get; init; } = "unknown";
}

public record CommandRequest
{
    public required string Type { get; init; } // Enable|Disable|UpdateSip|Restart
    public Dictionary<string, string>? Payload { get; init; }
}
