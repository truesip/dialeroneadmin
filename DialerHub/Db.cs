using Dapper;
using MySqlConnector;
using System.Data;

namespace DialerHub;

public class MariaDbOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}

public interface IDbFactory
{
    MySqlConnection Create();
}

public class MariaDbFactory : IDbFactory
{
    private readonly string _conn;
    public MariaDbFactory(IConfiguration cfg)
    {
        _conn = cfg["MariaDb:ConnectionString"] ?? string.Empty;
    }
    public MySqlConnection Create() => new MySqlConnection(_conn);
}

public static class DbInit
{
    public static async Task EnsureSchemaAsync(IDbFactory dbf)
    {
        using var conn = dbf.Create();
        await conn.OpenAsync();
        // Create tables if not exist
        var sql = @"
CREATE TABLE IF NOT EXISTS Agents (
  AgentId VARCHAR(100) PRIMARY KEY,
  MachineName VARCHAR(255) NOT NULL,
  IPs TEXT NULL,
  MACs TEXT NULL,
  Version VARCHAR(50) NULL,
  LastSeenUtc DATETIME NOT NULL,
  Status VARCHAR(32) NOT NULL,
  InstanceId VARCHAR(100) NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS Commands (
  Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  AgentId VARCHAR(100) NOT NULL,
  Type VARCHAR(50) NOT NULL,
  Payload TEXT NULL,
  Status VARCHAR(20) NOT NULL DEFAULT 'Pending',
  ClaimedByInstance VARCHAR(100) NULL,
  CreatedAtUtc DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UpdatedAtUtc DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  INDEX IX_Commands_Status (Status),
  INDEX IX_Commands_Agent (AgentId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
";
        await conn.ExecuteAsync(sql);
    }
}

public class AgentRepository
{
    private readonly IDbFactory _dbf;
    public AgentRepository(IDbFactory dbf) { _dbf = dbf; }

    public async Task UpsertAgentAsync(AgentHello hello, string instanceId)
    {
        using var conn = _dbf.Create();
        await conn.OpenAsync();
        var sql = @"
INSERT INTO Agents (AgentId, MachineName, IPs, MACs, Version, LastSeenUtc, Status, InstanceId)
VALUES (@AgentId, @MachineName, @IPs, @MACs, @Version, @Now, 'unknown', @InstanceId)
ON DUPLICATE KEY UPDATE
  MachineName = VALUES(MachineName),
  IPs = VALUES(IPs),
  MACs = VALUES(MACs),
  Version = VALUES(Version),
  LastSeenUtc = VALUES(LastSeenUtc),
  InstanceId = VALUES(InstanceId);
";
        await conn.ExecuteAsync(sql, new
        {
            hello.AgentId,
            hello.MachineName,
            IPs = System.Text.Json.JsonSerializer.Serialize(hello.IPs),
            MACs = System.Text.Json.JsonSerializer.Serialize(hello.MACs),
            hello.Version,
            Now = DateTime.UtcNow,
            InstanceId = instanceId
        });
    }

    public async Task UpdateHeartbeatAsync(string agentId, string status)
    {
        using var conn = _dbf.Create();
        await conn.OpenAsync();
        var sql = "UPDATE Agents SET LastSeenUtc=@Now, Status=@Status WHERE AgentId=@AgentId";
        await conn.ExecuteAsync(sql, new { AgentId = agentId, Now = DateTime.UtcNow, Status = status });
    }

    public async Task MarkDisconnectedIfOwnedAsync(string agentId, string instanceId)
    {
        using var conn = _dbf.Create();
        await conn.OpenAsync();
        var sql = "UPDATE Agents SET InstanceId=NULL, Status='unknown' WHERE AgentId=@AgentId AND InstanceId=@InstanceId";
        await conn.ExecuteAsync(sql, new { AgentId = agentId, InstanceId = instanceId });
    }

    public async Task<IEnumerable<AgentInfo>> ListAsync()
    {
        using var conn = _dbf.Create();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync("SELECT AgentId, MachineName, IPs, MACs, LastSeenUtc, Status FROM Agents ORDER BY AgentId");
        var list = new List<AgentInfo>();
        foreach (var r in rows)
        {
            string[] ips = Array.Empty<string>();
            string[] macs = Array.Empty<string>();
            try { ips = System.Text.Json.JsonSerializer.Deserialize<string[]>(r.IPs ?? "[]") ?? Array.Empty<string>(); } catch { }
            try { macs = System.Text.Json.JsonSerializer.Deserialize<string[]>(r.MACs ?? "[]") ?? Array.Empty<string>(); } catch { }
            list.Add(new AgentInfo
            {
                AgentId = r.AgentId,
                MachineName = r.MachineName,
                IPs = ips,
                MACs = macs,
                LastSeenUtc = r.LastSeenUtc,
                Status = r.Status
            });
        }
        return list;
    }
}

public class CommandRepository
{
    private readonly IDbFactory _dbf;
    public CommandRepository(IDbFactory dbf) { _dbf = dbf; }

    public async Task<long> EnqueueAsync(string agentId, CommandRequest cmd)
    {
        using var conn = _dbf.Create();
        await conn.OpenAsync();
        var sql = @"INSERT INTO Commands (AgentId, Type, Payload, Status) VALUES (@AgentId, @Type, @Payload, 'Pending'); SELECT LAST_INSERT_ID();";
        var id = await conn.ExecuteScalarAsync<long>(sql, new
        {
            AgentId = agentId,
            Type = cmd.Type,
            Payload = cmd.Payload != null ? System.Text.Json.JsonSerializer.Serialize(cmd.Payload) : null
        });
        return id;
    }

    public async Task<IEnumerable<(long Id, string AgentId, string Type, string? Payload)>> ClaimBatchAsync(string instanceId, int batchSize)
    {
        using var conn = _dbf.Create();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();

        // Claim commands for agents owned by this instance
        var claimSql = @"
UPDATE Commands c
JOIN Agents a ON a.AgentId = c.AgentId
SET c.Status = 'Claimed', c.ClaimedByInstance = @InstanceId
WHERE c.Status = 'Pending' AND a.InstanceId = @InstanceId
ORDER BY c.Id
LIMIT @Batch;
";
        await conn.ExecuteAsync(claimSql, new { InstanceId = instanceId, Batch = batchSize }, tx);

        var selectSql = "SELECT Id, AgentId, Type, Payload FROM Commands WHERE Status='Claimed' AND ClaimedByInstance=@InstanceId ORDER BY Id LIMIT @Batch";
        var rows = await conn.QueryAsync(selectSql, new { InstanceId = instanceId, Batch = batchSize }, tx);
        await tx.CommitAsync();

        return rows.Select(r => ((long)r.Id, (string)r.AgentId, (string)r.Type, (string?)r.Payload));
    }

    public async Task MarkSentAsync(long id)
    {
        using var conn = _dbf.Create();
        await conn.OpenAsync();
        await conn.ExecuteAsync("UPDATE Commands SET Status='Sent' WHERE Id=@Id", new { Id = id });
    }

    public async Task MarkFailedAsync(long id)
    {
        using var conn = _dbf.Create();
        await conn.OpenAsync();
        await conn.ExecuteAsync("UPDATE Commands SET Status='Failed' WHERE Id=@Id", new { Id = id });
    }
}
