using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace DialerHub;

public class MongoOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Database { get; set; } = "dialerhub";
}

public interface IMongoFactory
{
    IMongoDatabase GetDatabase();
}

public class MongoFactory : IMongoFactory
{
    private readonly MongoClient _client;
    private readonly string _dbName;
    public MongoFactory(IConfiguration cfg)
    {
        var cs = cfg["Mongo:ConnectionString"] ?? "mongodb://localhost:27017";
        _dbName = cfg["Mongo:Database"] ?? "dialerhub";
        _client = new MongoClient(cs);
    }
    public IMongoDatabase GetDatabase() => _client.GetDatabase(_dbName);
}

// DB models
internal class DBAgent
{
    [BsonId]
    public string AgentId { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string[] IPs { get; set; } = Array.Empty<string>();
    public string[] MACs { get; set; } = Array.Empty<string>();
    public string? Version { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public string Status { get; set; } = "unknown";
    public string? InstanceId { get; set; }
}

internal class DBCommand
{
    [BsonId]
    public ObjectId Id { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, string>? Payload { get; set; }
    public string Status { get; set; } = "Pending"; // Pending|Claimed|Sent|Failed
    public string? ClaimedByInstance { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class DbInit
{
    public static async Task EnsureSchemaAsync(IMongoFactory mf)
    {
        // Ensure indexes
        var db = mf.GetDatabase();
        var commands = db.GetCollection<DBCommand>("commands");
        var idx = new CreateIndexModel<DBCommand>(Builders<DBCommand>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.AgentId).Ascending(x => x.CreatedAtUtc));
        await commands.Indexes.CreateOneAsync(idx);
    }
}

public class AgentRepository
{
    private readonly IMongoCollection<DBAgent> _agents;
    public AgentRepository(IMongoFactory mf)
    {
        _agents = mf.GetDatabase().GetCollection<DBAgent>("agents");
    }

    public async Task UpsertAgentAsync(AgentHello hello, string instanceId)
    {
        var filter = Builders<DBAgent>.Filter.Eq(x => x.AgentId, hello.AgentId);
        var update = Builders<DBAgent>.Update
            .Set(x => x.MachineName, hello.MachineName)
            .Set(x => x.IPs, hello.IPs)
            .Set(x => x.MACs, hello.MACs)
            .Set(x => x.Version, hello.Version)
            .Set(x => x.LastSeenUtc, DateTime.UtcNow)
            .Set(x => x.InstanceId, instanceId)
            .SetOnInsert(x => x.Status, "unknown");
        await _agents.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
    }

    public Task UpdateHeartbeatAsync(string agentId, string status)
    {
        var filter = Builders<DBAgent>.Filter.Eq(x => x.AgentId, agentId);
        var update = Builders<DBAgent>.Update.Set(x => x.LastSeenUtc, DateTime.UtcNow).Set(x => x.Status, status);
        return _agents.UpdateOneAsync(filter, update);
    }

    public Task MarkDisconnectedIfOwnedAsync(string agentId, string instanceId)
    {
        var filter = Builders<DBAgent>.Filter.Eq(x => x.AgentId, agentId) & Builders<DBAgent>.Filter.Eq(x => x.InstanceId, instanceId);
        var update = Builders<DBAgent>.Update.Set(x => x.InstanceId, null).Set(x => x.Status, "unknown");
        return _agents.UpdateOneAsync(filter, update);
    }

    public async Task<IEnumerable<AgentInfo>> ListAsync()
    {
        var list = await _agents.Find(Builders<DBAgent>.Filter.Empty).ToListAsync();
        return list.Select(a => new AgentInfo
        {
            AgentId = a.AgentId,
            MachineName = a.MachineName,
            IPs = a.IPs ?? Array.Empty<string>(),
            MACs = a.MACs ?? Array.Empty<string>(),
            LastSeenUtc = a.LastSeenUtc,
            Status = a.Status
        });
    }
}

public class CommandRepository
{
    private readonly IMongoCollection<DBCommand> _cmds;
    private readonly IMongoCollection<DBAgent> _agents;
    public CommandRepository(IMongoFactory mf)
    {
        var db = mf.GetDatabase();
        _cmds = db.GetCollection<DBCommand>("commands");
        _agents = db.GetCollection<DBAgent>("agents");
    }

    public async Task<string> EnqueueAsync(string agentId, CommandRequest cmd)
    {
        var c = new DBCommand { AgentId = agentId, Type = cmd.Type, Payload = cmd.Payload, Status = "Pending", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        await _cmds.InsertOneAsync(c);
        return c.Id.ToString();
    }

    public async Task<IReadOnlyList<(string Id, string AgentId, string Type, string? Payload)>> ClaimBatchAsync(string instanceId, IEnumerable<string> agentIds, int batchSize)
    {
        var ids = agentIds?.ToArray() ?? Array.Empty<string>();
        if (ids.Length == 0) return Array.Empty<(string, string, string, string?)>();
        var list = new List<(string, string, string, string?)>();
        for (int i = 0; i < batchSize; i++)
        {
            var filter = Builders<DBCommand>.Filter.Eq(x => x.Status, "Pending") &
                         Builders<DBCommand>.Filter.In(x => x.AgentId, ids);
            var update = Builders<DBCommand>.Update.Set(x => x.Status, "Claimed").Set(x => x.ClaimedByInstance, instanceId).Set(x => x.UpdatedAtUtc, DateTime.UtcNow);
            var opts = new FindOneAndUpdateOptions<DBCommand> { Sort = Builders<DBCommand>.Sort.Ascending(x => x.CreatedAtUtc), ReturnDocument = ReturnDocument.After };
            var claimed = await _cmds.FindOneAndUpdateAsync(filter, update, opts);
            if (claimed == null) break;
            list.Add((claimed.Id.ToString(), claimed.AgentId, claimed.Type, claimed.Payload != null ? System.Text.Json.JsonSerializer.Serialize(claimed.Payload) : null));
        }
        return list;
    }

    public Task MarkSentAsync(string id)
    {
        var oid = ObjectId.Parse(id);
        var update = Builders<DBCommand>.Update.Set(x => x.Status, "Sent").Set(x => x.UpdatedAtUtc, DateTime.UtcNow);
        return _cmds.UpdateOneAsync(x => x.Id == oid, update);
    }

    public Task MarkFailedAsync(string id)
    {
        var oid = ObjectId.Parse(id);
        var update = Builders<DBCommand>.Update.Set(x => x.Status, "Failed").Set(x => x.UpdatedAtUtc, DateTime.UtcNow);
        return _cmds.UpdateOneAsync(x => x.Id == oid, update);
    }
}
