using Microsoft.AspNetCore.HttpOverrides;
using DialerHub;

var builder = WebApplication.CreateBuilder(args);

// Unique id for this running instance (helps debug load-balancer behavior)
var instanceId = Environment.GetEnvironmentVariable("HOSTNAME") ?? Guid.NewGuid().ToString("n");

// SignalR (no Redis backplane; commands are routed via MariaDB queue)
builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// CORS: restrict to configured origins (comma-separated) via Cors:AllowedOrigins env/appsettings
var allowedOriginsCsv = builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty;
var allowedOrigins = allowedOriginsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddPolicy("Default", p =>
{
    if (allowedOrigins.Length > 0)
    {
        p.WithOrigins(allowedOrigins)
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials();
    }
    else
    {
        // Block all browser origins by default
        p.SetIsOriginAllowed(_ => false);
    }
}));

builder.Services.AddSingleton<DialerHub.AgentRegistry>();
// Respect X-Forwarded-* from DigitalOcean/ingress
builder.Services.Configure<ForwardedHeadersOptions>(opts =>
{
    opts.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    opts.KnownNetworks.Clear();
    opts.KnownProxies.Clear();
});

// MongoDB services
builder.Services.AddSingleton<IMongoFactory, MongoFactory>();
builder.Services.AddSingleton<AgentRepository>();
builder.Services.AddSingleton<CommandRepository>();

// Host identity + dispatcher
builder.Services.AddSingleton(new HostInstance(instanceId));
builder.Services.AddHostedService<CommandDispatcher>();

var app = builder.Build();

app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup").LogInformation("Instance {InstanceId} starting", instanceId);

// Initialize collections / indexes on startup
var mongo = app.Services.GetRequiredService<IMongoFactory>();
await DbInit.EnsureSchemaAsync(mongo);

// Must run before HTTPS redirection so Scheme is set from proxy headers
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseCors("Default");
app.MapControllers();
app.MapHub<DialerHub.AgentHub>("/hubs/agent");
app.MapGet("/", () => $"DialerHub online ({instanceId})");
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGet("/whoami", () => Results.Ok(new { instanceId, now = DateTime.UtcNow }));

app.Run();
