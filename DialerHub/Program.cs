using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
// CORS: relax by default; in prod, restrict to your Admin origins
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));
builder.Services.AddSingleton<DialerHub.AgentRegistry>();
// Respect X-Forwarded-* from DigitalOcean/ingress
builder.Services.Configure<ForwardedHeadersOptions>(opts =>
{
    opts.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    opts.KnownNetworks.Clear();
    opts.KnownProxies.Clear();
});

var app = builder.Build();

// Must run before HTTPS redirection so Scheme is set from proxy headers
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseCors();
app.MapControllers();
app.MapHub<DialerHub.AgentHub>("/hubs/agent");
app.MapGet("/", () => "DialerHub online");
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();
