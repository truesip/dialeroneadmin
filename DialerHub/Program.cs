using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

// Must run before HTTPS redirection so Scheme is set from proxy headers
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseCors("Default");
app.MapControllers();
app.MapHub<DialerHub.AgentHub>("/hubs/agent");
app.MapGet("/", () => "DialerHub online");
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();
